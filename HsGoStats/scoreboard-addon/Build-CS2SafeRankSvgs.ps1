[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
& (Join-Path $PSScriptRoot 'Build-CS2SafeRankSvgs.Core.ps1')
