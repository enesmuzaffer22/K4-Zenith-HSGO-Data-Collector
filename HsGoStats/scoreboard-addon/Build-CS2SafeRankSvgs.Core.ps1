[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore

$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$sourceDirectory = Join-Path $PSScriptRoot '..\rank-svg'
$outputDirectory = Join-Path $PSScriptRoot 'content\csgo_addons\hsgo_ranks\panorama\images\icons\skillgroups'

$rankFiles = @(
    'recruit.svg',
    'soldier.svg',
    'trooper.svg',
    'veteran.svg',
    'warrior.svg',
    'warlord.svg',
    'phantom.svg',
    'supreme.svg',
    'conquer.svg',
    'immortal.svg'
)

function Format-Coordinate {
    param([double] $Value)

    if ([Math]::Abs($Value) -lt 0.00005) {
        $Value = 0
    }

    return $Value.ToString('0.####', $invariant)
}

function Convert-Point {
    param(
        [double] $X,
        [double] $Y,
        [double] $Scale,
        [double] $OffsetY
    )

    return [System.Windows.Point]::new(($X * $Scale), (($Y * $Scale) + $OffsetY))
}

function Format-Point {
    param($Point)

    return "$(Format-Coordinate $Point.X) $(Format-Coordinate $Point.Y)"
}

function Get-Fill {
    param(
        [System.Xml.XmlElement] $Element,
        [hashtable] $ClassFills
    )

    $fill = $Element.GetAttribute('fill')
    if ($fill) {
        return $fill
    }

    $className = $Element.GetAttribute('class')
    if ($className -and $ClassFills.ContainsKey($className)) {
        return $ClassFills[$className]
    }

    throw "No fill was found for <$($Element.LocalName)> in class '$className'."
}

function Convert-PathData {
    param(
        [string] $Data,
        $Matrix
    )

    $geometry = [System.Windows.Media.Geometry]::Parse($Data).Clone()
    $geometry.Transform = [System.Windows.Media.MatrixTransform]::new($Matrix)
    $flattened = $geometry.GetFlattenedPathGeometry(
        0.003,
        [System.Windows.Media.ToleranceType]::Absolute
    )

    $commands = [System.Collections.Generic.List[string]]::new()
    foreach ($figure in $flattened.Figures) {
        $commands.Add("M $(Format-Point $figure.StartPoint)")

        foreach ($segment in $figure.Segments) {
            if ($segment -is [System.Windows.Media.PolyLineSegment]) {
                foreach ($point in $segment.Points) {
                    $commands.Add("L $(Format-Point $point)")
                }
            }
            elseif ($segment -is [System.Windows.Media.LineSegment]) {
                $commands.Add("L $(Format-Point $segment.Point)")
            }
            else {
                throw "Unexpected segment type after flattening: $($segment.GetType().FullName)"
            }
        }

        if ($figure.IsClosed) {
            $commands.Add('Z')
        }
    }

    return $commands -join ' '
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

for ($index = 0; $index -lt $rankFiles.Count; $index++) {
    $sourcePath = Join-Path $sourceDirectory $rankFiles[$index]
    [xml] $document = Get-Content -LiteralPath $sourcePath -Raw

    $root = $document.DocumentElement
    $viewBoxValues = [regex]::Matches($root.GetAttribute('viewBox'), '-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?') |
        ForEach-Object { [double]::Parse($_.Value, $invariant) }

    if ($viewBoxValues.Count -ne 4) {
        throw "Invalid viewBox in $sourcePath"
    }

    $sourceWidth = $viewBoxValues[2]
    $sourceHeight = $viewBoxValues[3]
    $scale = 32.0 / $sourceWidth
    $offsetY = (13.0 - ($sourceHeight * $scale)) / 2.0

    $matrix = [System.Windows.Media.Matrix]::Identity
    $matrix.Scale($scale, $scale)
    $matrix.Translate(0, $offsetY)

    $classFills = @{}
    foreach ($styleNode in $root.SelectNodes('.//*[local-name()="style"]')) {
        foreach ($match in [regex]::Matches($styleNode.InnerText, '\.([A-Za-z0-9_-]+)\s*\{[^}]*fill\s*:\s*([^;}{]+)')) {
            $classFills[$match.Groups[1].Value] = $match.Groups[2].Value.Trim()
        }
    }

    $outputPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($element in $root.SelectNodes('.//*[local-name()="rect" or local-name()="polygon" or local-name()="path"]')) {
        $fill = Get-Fill $element $classFills
        $pathData = $null

        switch ($element.LocalName) {
            'rect' {
                $x = [double]::Parse($element.GetAttribute('x'), $invariant)
                $y = [double]::Parse($element.GetAttribute('y'), $invariant)
                $width = [double]::Parse($element.GetAttribute('width'), $invariant)
                $height = [double]::Parse($element.GetAttribute('height'), $invariant)
                $topLeft = Convert-Point $x $y $scale $offsetY
                $topRight = Convert-Point ($x + $width) $y $scale $offsetY
                $bottomRight = Convert-Point ($x + $width) ($y + $height) $scale $offsetY
                $bottomLeft = Convert-Point $x ($y + $height) $scale $offsetY
                $pathData = "M $(Format-Point $topLeft) L $(Format-Point $topRight) L $(Format-Point $bottomRight) L $(Format-Point $bottomLeft) Z"
            }
            'polygon' {
                $values = [regex]::Matches($element.GetAttribute('points'), '-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?') |
                    ForEach-Object { [double]::Parse($_.Value, $invariant) }
                if (($values.Count % 2) -ne 0) {
                    throw "Odd polygon coordinate count in $sourcePath"
                }

                $commands = [System.Collections.Generic.List[string]]::new()
                for ($pointIndex = 0; $pointIndex -lt $values.Count; $pointIndex += 2) {
                    $point = Convert-Point $values[$pointIndex] $values[$pointIndex + 1] $scale $offsetY
                    $command = if ($pointIndex -eq 0) { 'M' } else { 'L' }
                    $commands.Add("$command $(Format-Point $point)")
                }
                $commands.Add('Z')
                $pathData = $commands -join ' '
            }
            'path' {
                $pathData = Convert-PathData $element.GetAttribute('d') $matrix
            }
        }

        $outputPaths.Add("  <path fill=`"$fill`" d=`"$pathData`"/>")
    }

    $outputName = 'skillgroup{0:D8}.svg' -f (19010001 + $index)
    $outputPath = Join-Path $outputDirectory $outputName
    $output = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<svg version="1.1" xmlns="http://www.w3.org/2000/svg" x="0px" y="0px" width="32px" height="13px" viewBox="0 0 32 13" enable-background="new 0 0 32 13" xml:space="preserve">'
        $outputPaths
        '</svg>'
        ''
    ) -join "`r`n"

    Set-Content -LiteralPath $outputPath -Value $output -Encoding utf8 -NoNewline
    Write-Host "Generated $outputName from $($rankFiles[$index])"
}
