# HSGo scoreboard rank addon source

Package version: **1.5.3** (CS2-safe absolute path geometry)

The SVG sources are ready for the CS2 Workshop Tools resource compiler under:

`content/csgo_addons/hsgo_ranks/panorama/images/icons/skillgroups/`

The plugin default `rating.scoreboard.skillGroupBaseId` is `19010001`.

| Skill group | Tier |
|---:|---|
| 19010001 | Recruit |
| 19010002 | Soldier |
| 19010003 | Trooper |
| 19010004 | Veteran |
| 19010005 | Warrior |
| 19010006 | Warlord |
| 19010007 | Phantom |
| 19010008 | Supreme |
| 19010009 | Conquer |
| 19010010 | Immortal |

## Build and distribution

1. On Windows, install **Counter-Strike 2 Workshop Tools** from the CS2 DLC list.
2. Launch Workshop Tools, create a new addon named exactly `hsgo_ranks`, then close the tools.
3. Merge this package's `content/csgo_addons/hsgo_ranks` directory into the local CS2 `content/csgo_addons/hsgo_ranks` directory.
4. Back up `game/csgo/gameinfo.gi`. Under `AddonConfig` -> `VpkDirectories`, add this line if it is not already present:

   ```text
   "include" "panorama/images/icons/skillgroups"
   ```

   This is a temporary Workshop-packaging change only. After the Workshop upload/re-upload finishes, close Workshop Tools and immediately restore the original `gameinfo.gi` (or remove only this added line) before launching normal CS2. A modified base `gameinfo.gi` can make VAC session verification fail. Never upload this modified base file to the game server. CS2 updates may also overwrite the local packaging-list change, so check it again before later Workshop re-uploads.
5. Compile/publish the addon with Workshop Tools. The compiled files must become `skillgroup19010001.vsvg_c` through `skillgroup19010010.vsvg_c` under the corresponding `game/csgo_addons/hsgo_ranks/panorama/images/icons/skillgroups` path. Keep the generated KV3 `addoninfo.txt` non-playable. Do not mix the older KeyValues1 `"AddonInfo" { ... }` wrapper with a KV3 header. Its body can be:

   ```text
   <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
   {
       IsTemplate = 0
       IsPlayable = 0
   }
   ```

   `resourcecompiler.exe` expects `-game` to point to the directory containing `gameinfo.gi`, not to the file itself. A PowerShell example is:

   ```powershell
   & "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\resourcecompiler.exe" -i "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\hsgo_ranks\panorama\images\icons\skillgroups\*.svg" -game "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo" -f -nop4
   ```
6. In Asset Browser open **Tools -> Counter-Strike 2 Workshop Manager**, create a new item, select the `hsgo_ranks` addon and publish it. Wait until Steam has made the item downloadable, then copy its numeric Workshop ID.
7. Install/configure MultiAddonManager on the server and add the ID to `mm_client_extra_addons`, because these rank images are client-side resources. Restart/change map and have existing players reconnect.
8. Confirm connecting clients receive the addon before enabling the plugin scoreboard setting.
9. If the skill-group IDs are changed, rename all ten SVG files and change `skillGroupBaseId` in the database `rating` setting together.

The original design files remain in `HsGoStats/rank-svg`. This directory is a deployment copy with the names required by the native scoreboard.

## Panorama SVG compatibility

The deployment SVGs intentionally use direct `fill` attributes, a native `viewBox="0 0 32 13"`, and only absolute `M`, `L`, and `Z` path commands. They contain no style/class references, transforms, groups, curves, arcs, rectangles, polygons, or compressed path-number syntax. Valve's resource compiler may accept those richer forms while the Panorama runtime still reports `Unable to parse SVG`. Run `Build-CS2SafeRankSvgs.ps1` after changing an original design in `HsGoStats/rank-svg`, then compile and upload the regenerated files.
