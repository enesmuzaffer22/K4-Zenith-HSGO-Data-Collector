# Test report

## 2026-11-26 — Rank, scoreboard ve sıralama sürümü 1.5.0

| Check | Result |
|---|---|
| Release build | PASS — 0 warning, 0 error |
| Unit tests | PASS — 64/64 |
| Recruit–Immortal 10 tier sınırları | PASS |
| Aktif sezonda sezon TP seçimi | PASS |
| Sezon verisi/yokluğunda genel TP fallback | PASS |
| Scoreboard config doğrulaması | PASS |
| `!sezon` ve `!siralama` renkli çıktı yolu | PASS — derleme/statik denetim |
| Genel ve sezon ilk 10 sorguları | PASS — derleme/statik SQL denetimi |
| SVG dosya sayısı ve skillgroup eşlemesi | PASS — 10/10 |
| Panorama SVG sadeleştirme | PASS — 10/10 geçerli XML; SVG 1.1 + doğal 32x13 viewBox; yalnızca doğrudan fill ve mutlak M/L/Z path geometrisi; style/class/defs/group/transform/eğri/arc yok |
| CS2 `resourcecompiler.exe` doğrulaması | PASS — 10 compiled, 0 failed |
| 1.5.0 DB setting migration | PASS — statik SQL denetimi |
| Plugin release archive | `HsGoStats-v1.5.0.zip`, 595051 bytes, SHA-256 `EE4F84132EA9E5FD0382C111D11D7F84057360ED0AA3622F0D8EDCC3D180B95C` |
| Rank addon source archive | `HsGoStats-rank-addon-src-v1.5.3.zip`, 28714 bytes, SHA-256 `BCFDB5B909BFA0E8EAEB46EBD767495F4B0BE1985C2AF3B12B65385D605C8E72` |
| Gerçek CS2 TAB, addon dağıtımı ve MySQL sorguları | NOT TESTED — staging sunucusu/veritabanı gerekli |

Sandbox içindeki iki VSTest denemesi testhost IPC bağlantısında 90/180 saniye zaman aşımına uğradı. Aynı derlenmiş paket izinli sandbox dışı çalıştırmada 64/64 geçti; assertion hatası oluşmadı.

## 2026-11-24 — DB ayarları ve oturum telemetrisi sürümü 1.3.0

| Check | Result |
|---|---|
| Release build | PASS — 0 warning, 0 error |
| Unit tests | PASS — 40/40 |
| Yerel config'in yalnız bootstrap alanları taşıması | PASS |
| Eski 1.2 JSON bölümlerinin ilk DB seed'e aktarılması | PASS |
| Runtime ayar doğrulaması | PASS |
| `hsgo_plugin_settings` schema v5 | PASS |
| `hsgo_player_sessions` schema ve indeksleri | PASS |
| Session open/heartbeat/close dayanıklı JSON round-trip | PASS |
| Son 24 saat unique/session/ortalama sorgusu | PASS — statik sorgu denetimi |
| Örnek ve yayın bootstrap JSON parse | PASS |
| C4 drop/pick event handler veya XP | PASS — kapsam dışında, eklenmedi |
| Release archive | `HsGoStats-v1.3.0.zip`, 577236 bytes, SHA-256 `46476EB6A2E1A40E4DA9BC4FA8AF57E6A2FC46D59500EC4C1B5BCD8CDBAA71A4` |
| Gerçek MySQL admin update ve CS2 disconnect/crash | NOT TESTED — staging sunucusu/veritabanı gerekli |

DB-backed ayarların gerçek MySQL transaction davranışı ile aktif oyuncu oturum süreleri staging ortamı olmadan doğrulanamaz. Derleme, birim testleri, DDL ve sorgu denetimleri geçmiştir; canlı başarı iddiası yapılmaz.

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

## 2026-11-26 — Scoreboard rank reveal düzeltmesi 1.5.1

- TAB rank alanlarının mevcut DB yenileme aralığından bağımsız olarak iki saniyede bir görünür tutulması eklendi.
- Sabit user-message ID `350` yerine `CCSUsrMsg_ServerRankRevealAll` adı kullanılıyor.
- Reveal mesajı çözümlenemezse istatistik toplama devam eder; hata `revealBlocked` tanısı ve tek log kaydıyla sınırlandırılır.
- `dotnet build HsGoStats/src/HsGoStats.csproj -c Release --no-restore`: başarılı, 0 uyarı, 0 hata.
- `dotnet test` derleme ve test-host başlangıcını tamamladı fakat mevcut test çalıştırıcısı keşif sonrasında sonuç üretmeden bekledi; süreç sonlandırıldı.

## 2026-11-26 — TAB basışına bağlı rank reveal düzeltmesi 1.5.2

- 1.5.1'in iki saniyelik global reveal yayını canlı testte ikonun düzenli biçimde görünüp kaybolmasına neden olduğu için kaldırıldı.
- `OnPlayerButtonsChanged` ile gerçek TAB basışının yükselen kenarı dinleniyor.
- `CCSUsrMsg_ServerRankRevealAll` yalnız TAB'a basan oyuncuya gönderiliyor; yaklaşım güncel FakeRanks-RevealAll Metamod uygulamasıyla eşleştirildi.
- Rank henüz oyuncuya uygulanmadıysa TAB basışı anında DB yenilemesi isteniyor ve sonraki TAB açılışında reveal uygulanıyor.

## 2026-11-26 — Gecikmeli TAB reveal düzeltmesi 1.5.3

- Canlı 1.5.2 testinde plugin sürümü ve blok durumları doğru olmasına rağmen reveal mesajının pre-think aşamasında erken gönderildiği gözlendi.
- TAB basışından 0,2 saniye sonra beklenen skillgroup değerleri yeniden yazılıp `SetStateChanged` ile replikasyona işaretleniyor.
- Reveal mesajı takip eden frame'de yalnız TAB'a basan oyuncuya gönderiliyor.
- Rank değişiminde yapılan global reveal kaldırıldı; istemci yalnız gerçek TAB açılışında tetikleniyor.

## 2026-11-26 — Metamod reveal sağlayıcısına geçiş 1.5.4

- Canlı testte CounterStrikeSharp üzerinden gönderilen reveal mesajı doğru zamanda ulaştığı halde ikon istemci tarafından hemen gizlendi.
- HsGoStats içindeki TAB listener ve user-message gönderimi tamamen kaldırıldı.
- Plugin yalnız `CompetitiveWins`, `CompetitiveRankType` ve `CompetitiveRanking` alanlarını yönetir.
- TAB reveal sorumluluğu K4-System-MMRanks'in de önerdiği `FakeRanks-RevealAll` Metamod eklentisine bırakıldı.

## 2026-11-26 — Scoreboard netprop kalıcılığı 1.5.5

- `FakeRanks-RevealAll` yüklüyken ikonun kısa süre görünüp kaybolması reveal katmanını eledi ve competitive rank alanlarının geri sıfırlandığını gösterdi.
- Aktif oyuncuların beklenen skillgroup değerleri DB yenilemesinden ayrı bir bellek önbelleğinde tutuluyor.
- Her tick yalnız alanları beklenen değerden farklı olan oyuncular yeniden yazılıyor; tick döngüsünde DB veya başka I/O yapılmıyor.
- Competitive görünürlük değeri güncel LevelRanksFakeRank ile eşleşecek şekilde `CompetitiveWins=777` olarak ayarlandı.
- `css_hsgo_status` çıktısına `maintained` ve `repairs` sayaçları eklendi.
