# Test report — 2026-11-23

| Check | Command / evidence | Result |
|---|---|---|
| Locked restore | `dotnet restore HsGoStats/HsGoStats.slnx --locked-mode` | PASS |
| Release build | `dotnet build HsGoStats/HsGoStats.slnx -c Release --no-restore` | PASS — 0 warnings, 0 errors |
| Unit tests | `dotnet test HsGoStats/tests/HsGoStats.Tests.csproj -c Release --no-build` | PASS — 12/12 |
| Season boundaries | 1 Dec, 31 Dec, 30 Jan assertions | PASS |
| Four tracking combinations | theory cases for Enabled/Preseason/active | PASS |
| Suicide rule | death counter +1, total score -10 | PASS |
| Kill + HS scoring | expected +15 | PASS |
| Invalid default credentials | config validator assertion | PASS |
| Season leaderboard blacklist routing | blacklisted player keeps global target and loses season target | PASS |
| Invalid blacklist SteamID64 | config validator assertion | PASS |
| Deferred blacklist activation | Season 1 remains visible; exclusion begins at configured Season 2 boundary | PASS |
| Single plugin entry / forbidden commands | source inspection: one BasePlugin, no command/menu registration | PASS |
| Schema safety | source/SQL inspection: only `hsgo_`, no DROP/TRUNCATE | PASS |
| MySQL empty DB and DDL permissions | needs provider DB | NOT TESTED |
| Real CS2 event counts and lifecycle | needs CS2 server and players | NOT TESTED |
| DB outage/game-thread freeze | requires instrumented server | NOT TESTED (design has no game-thread I/O) |
| Retry/replay duplicate integration | requires MySQL fault injection | NOT TESTED (transaction/idempotency implemented) |
| Multi-season downtime/concurrent instances | requires MySQL integration | NOT TESTED (lock/unique constraints implemented) |
| Prooyun/WeaponPaints compatibility | plugin inventory/server unavailable | NOT TESTED |
| Ban import lifecycle | provider MySQL unavailable | NOT TESTED; SQL supplied |
| External web read access | provider/network credentials unavailable | NOT TESTED |
| Release archive | `HsGoStats-v1.0.1.zip`, 517400 bytes, SHA-256 `F2F067A4D2D63357EFD4EC5523C081FA7B2F7F0728BDB0D7442E4E5ACC6C3376` | PASS |

İlk test koşusunda test runtime'ında CounterStrikeSharp API kopyası eksik olduğu için 8 PASS / 1 FAIL alındı; test projesine açık runtime referansı eklendi. Gelecek sezon etkinleşen blacklist testleriyle genişletilen son takım 12/12 PASS verdi. Bu geçmiş saklanmıştır; sahte canlı ortam başarısı iddia edilmez.
