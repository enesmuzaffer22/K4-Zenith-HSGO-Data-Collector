# Configuration

Config yolu: `addons/counterstrikesharp/configs/plugins/HsGoStats/HsGoStats.json`. Tüm alanları içeren güvenli örnek `examples/HsGoStats.json` dosyasındadır.

- `ConfigVersion`: yalnız `1` kabul edilir.
- `Enabled`: istatistik event üretimini kapatır; schema/ban arşivini silmez.
- `ServerId`: aynı DB'deki sunucu boyutu, 1–64 karakter.
- `Season.Enabled`: sezon satırlarını ve geçişi açar.
- `Season.AutoTransition`: false ise mevcut sezon bittikten sonra yeni sezon açılmaz. Manuel güvenli yöntem DB yedeği alıp gelecekte başlayacak tek bir `scheduled` satırı eklemek veya config'i true yapıp restart etmektir; tamamlanmış satırlar değiştirilmez.
- `DurationDays`: takvim ayı değil, kesin `N × 24 saat`.
- `NameFormat`: `{number}` içermelidir.
- `InitialSeasonNumber`, `StartDate`: yalnız ilk bootstrap tohumu. Sonrasında DB değerleri doğrudur.
- `Timezone`: IANA kimliği doğrulanır; kıyaslamalar UTC'dir.
- `PreseasonTracking`: aktif sezon yokken yalnız genel istatistik toplar. Aktif sezonda false olsa da genel + sezon yazılır.
- `SeasonOverrideEndDate`: yalnız o anda aktif sezon için, gelecekteki güvenli bir bitişe tek seferlik uygulanır. Audit anahtarı `hsgo_season_settings` içine yazılır; scheduled gelecek yeni sınırdan üretilir. Geçmiş/şüpheli değer reddedilir.
- `Season.LeaderboardBlacklistSteamIds`: string biçiminde SteamID64 listesi. Bir oyuncu aktif sezon sırasında listeye eklenirse mevcut ve geçmiş sezonlardaki sırası/verisi korunur; dışlama bir sonraki sezon numarasından itibaren başlar. Bundan sonraki sezonlarda oyuncu, harita ve silah sezon satırları yazılmaz. Genel/all-time istatistikler daima tutulur. İlk sezon başlamadan eklenirse Season 1'den itibaren geçerlidir. Örnek: `["76561198000000001"]`. Geçersiz SteamID64 config'i reddeder, tekrarlar uyarı üretir. Değişiklikten sonra plugin/sunucu yeniden başlatılmalıdır.
- `Scoring`: kill 10, HS ek 5, assist 4, death -3, plant 5, defuse 10, round win 3, suicide toplam -10. İntiharda death sayacı artar ama -3 ayrıca uygulanmaz. Negatif skor desteklenir.
- `Database`: MySqlConnector seçenekleri. `Username`/`Password=CHANGE_ME` hatadır. `SslMode=Preferred` varsayılandır; `None` uyarı üretir.
- `Tracking`: sayaç/event bayrakları. `ShotsAndHits` maliyet ve event güvenilirliği nedeniyle varsayılan kapalıdır. Round katılımı round başında T/CT takımında bulunan oyuncularla sınırlıdır.
- `Bans`: bu sürümde yalnız schema/manual SQL vardır. Auto import, native event ve enforcement true verilirse config reddedilir.
- `Persistence`: bounded queue, batch, retry/backoff ve spool ayarları. Queue doluysa oyun thread'i beklemez; olay düşürülür ve loglanır.
- `Logging.IncludePlayerNames`: false iken isim DB'ye gönderilmez. SteamID64 kimliktir.

Bootstrap sonrası `StartDate`, `DurationDays`, `InitialSeasonNumber` veya `NameFormat` farkı mevcut sezonları değiştirmez ve uyarı loglanır. Puan katsayısı değişikliği geçmiş skorları yeniden hesaplamaz; her event etkin politikanın `scoring_version` hash'ini taşır.
