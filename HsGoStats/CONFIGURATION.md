# Configuration

Config yolu: `addons/counterstrikesharp/configs/plugins/HsGoStats/HsGoStats.json`. Tüm alanları içeren güvenli örnek `examples/HsGoStats.json` dosyasındadır.

- `ConfigVersion`: yalnız `1` kabul edilir.
- `Enabled`: istatistik event üretimini kapatır; schema/ban arşivini silmez.
- `ServerId`: aynı DB'deki sunucu boyutu, 1–64 karakter.
- `Season.Enabled`: elle tanımlanan sezon takvimini açar.
- `Season.Schedule`: sezonların kronolojik listesidir. Her kayıt `Name`, `StartsAt` ve `EndsAt` alanlarını içerir. Tarihler `Z` veya açık UTC ofsetli ISO-8601 olmalı, bitiş başlangıçtan sonra gelmeli ve sezonlar çakışmamalıdır. Sezonlar arasında boşluk bırakılabilir. Bitiş sınırı dışlayıcıdır: `[StartsAt, EndsAt)`.
- Otomatik sezon üretimi/yenilemesi yoktur. Yeni sezon için listeye yeni bir kayıt eklenip plugin veya sunucu yeniden başlatılır. Tamamlanmış kayıtlar geçmiş bütünlüğü için listede aynı sırada tutulmalıdır; tamamlanmış bir sezonun DB sınırları sonradan config ile değiştirilmez.
- `Timezone`: IANA kimliği doğrulanır; kıyaslamalar UTC'dir.
- `PreseasonTracking`: aktif sezon yokken yalnız genel istatistik toplar. Aktif sezonda false olsa da genel + sezon yazılır.
- `Season.LeaderboardBlacklistSteamIds`: string biçiminde SteamID64 listesi. Bir oyuncu aktif sezon sırasında listeye eklenirse mevcut ve geçmiş sezonlardaki sırası/verisi korunur; dışlama bir sonraki sezon numarasından itibaren başlar. Bundan sonraki sezonlarda oyuncu, harita ve silah sezon satırları yazılmaz. Genel/all-time istatistikler daima tutulur. İlk sezon başlamadan eklenirse Season 1'den itibaren geçerlidir. Örnek: `["76561198000000001"]`. Geçersiz SteamID64 config'i reddeder, tekrarlar uyarı üretir. Değişiklikten sonra plugin/sunucu yeniden başlatılmalıdır.
- `Scoring`: kill 10, HS ek 5, assist 4, death -3, plant 5, defuse 10, round win 3, suicide toplam -10. İntiharda death sayacı artar ama -3 ayrıca uygulanmaz. Negatif skor desteklenir.
- `GameXp`: eski `Scoring/score` sayaçlarından bağımsız HSR olay ekonomisidir. Özel kill türleri üst üste eklenmez; geçerli değerlerin en yükseği seçilir. C4 drop/pickup bu sürümde uygulanmaz.
- `Rating`: minimum HSR, demotion davranışı ve sıralı sekiz tier eşiği/adı/HEX rengidir. Tier kalıcı yazılmaz; bakiyeden hesaplanır. Tier tablosu web için MySQL'e senkronlanır.
- `WeeklyMissions`: ödül hedefleri, dışlanan silahlar ve 100 görevlik tam katalogdur. Görevler yalnız aktif sezon varken atanır ve ilerler; sezon bittiği anda açık görevler de kapanır. Katalogdaki görev ID, kategori, olay, hedef ve ödül değiştirilebilir; değişiklik yalnız yeni atamalara uygulanır.
- `WeeklyMissions.Rotation`: varsayılan `Europe/Istanbul`, Pazartesi `00:00`, kategori başına bir görevdir. Sezon haftanın ortasında başlarsa ilk pencere tam sezon başlangıcında, sezon haftanın ortasında biterse son pencere tam sezon bitişinde kesilir. Her pencere `season_id` ile saklanır; iki sezon aynı takvim haftasına denk gelse bile görevleri ayrıdır.
- `ChatCommands`: ASCII `!sezon` / `!gorevler`, özel yanıt ve gerçek raund başlangıcı hatırlatmasıdır. Türkçe karakterli alias eklenmemelidir.
- `Database`: MySqlConnector seçenekleri. `Username`/`Password=CHANGE_ME` hatadır. `SslMode=Preferred` varsayılandır; `None` uyarı üretir.
- `Tracking`: sayaç/event bayrakları. `ShotsAndHits` maliyet ve event güvenilirliği nedeniyle varsayılan kapalıdır. Round katılımı round başında T/CT takımında bulunan oyuncularla sınırlıdır.
- `Bans`: bu sürümde yalnız schema/manual SQL vardır. Auto import, native event ve enforcement true verilirse config reddedilir.
- `Persistence`: bounded queue, batch, retry/backoff ve spool ayarları. Queue doluysa oyun thread'i beklemez; olay düşürülür ve loglanır.
- `Logging.IncludePlayerNames`: false iken isim DB'ye gönderilmez. SteamID64 kimliktir.

Aktif sezonun adı ve gelecekteki bitişi config ile güncellenebilir; aktif sezonun başlangıcı geriye dönük taşınmaz. Tamamlanmış sezonun adı ve sınırlarında farklılık varsa DB geçmişi korunur ve uyarı loglanır. Puan katsayısı değişikliği geçmiş skorları yeniden hesaplamaz; her event etkin politikanın `scoring_version` hash'ini taşır.
