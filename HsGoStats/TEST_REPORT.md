# Test report

## 2026-11-24 — Manuel sezon takvimi sürümü 1.2.0

| Check | Result |
|---|---|
| Release build | PASS — 0 warning, 0 error |
| Unit tests | PASS — 36/36 |
| Özel sezon adı + kesin başlangıç/bitiş | PASS |
| Çakışan sezon ve ofsetsiz tarih reddi | PASS |
| Sezonlar arasındaki boşluk | PASS |
| Başlangıç dahil / bitiş hariç sınırlar | PASS |
| İlk görev haftasının sezon başlangıcında kırpılması | PASS |
| Son görev haftasının sezon bitişinde kırpılması | PASS |
| Sezon dışında haftalık pencere reddi | PASS |
| Örnek ve yayın JSON parse | PASS |
| C4 drop/pick event handler veya XP | PASS — kapsam dışında, eklenmedi |
| Release archive | `HsGoStats-v1.2.0.zip`, 565117 bytes, SHA-256 `B4DF59AA9DFF2E2C51C4ABA236F05B37565A48CAB32E15B0E29F9EDC500A47F8` |
| Gerçek CS2 ve MySQL takvim/DDL entegrasyonu | NOT TESTED — staging sunucusu/veritabanı gerekli |

Test paketi sandbox dışındaki izinli VSTest çalıştırmasında 36/36 geçti. MySQL repository davranışı statik olarak ve derleme ile doğrulandı; canlı DB iddiası yapılmaz.

## 2026-11-24 — HSR ve haftalık görev sürümü 1.1.0

| Check | Result |
|---|---|
| Release build | PASS — 0 warning, 0 error |
| Unit tests | PASS — 30/30 |
| 100 görev ID / 9 kategori | PASS |
| Katalog toplam ödülü | PASS — 17.145 XP |
| Tier eşik altı/eşiği/üstü ve Legend üstü | PASS |
| Europe/Istanbul Pazartesi sınırı | PASS |
| 33/34/35/51/52 özel kuralları | PASS |
| Scoped görevde NoScope reddi | PASS |
| 35/52 farklı raund ve tek ilerleme | PASS |
| Raundluk flash ilerlemesinin replace davranışı | PASS |
| C4 drop/pick event handler veya XP | PASS — kapsam dışında, eklenmedi |
| Release archive | `HsGoStats-v1.1.0.zip`, 565137 bytes, SHA-256 `93F8ED48AF47BC2689C3CB8BB53821EA48475317917EAD113393503C4FA89A89` |
| Gerçek CS2 ve MySQL fault-injection | NOT TESTED — staging sunucusu/veritabanı gerekli |

Sandbox içindeki ilk VSTest denemesi testhost IPC kısıtı nedeniyle bağlanamadı. Aynı derlenmiş test paketi sandbox dışında çalıştırıldı ve 30/30 geçti.

## 2026-11-23 — İstatistik toplayıcı sürümü 1.0.1 (tarihsel)

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
