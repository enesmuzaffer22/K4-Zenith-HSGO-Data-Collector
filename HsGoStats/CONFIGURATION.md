# Configuration

## Yerel bootstrap JSON

`addons/counterstrikesharp/configs/plugins/HsGoStats/HsGoStats.json` artık yalnız veritabanına bağlanmak için gereken alanları içerir:

- `ConfigVersion`: yalnız `1` kabul edilir.
- `ServerId`: aynı veritabanındaki sunucuyu ayıran 1–64 karakterlik kimliktir.
- `SettingsStore.SeedMissingRows`: true ise eksik ayar satırlarını güvenli ürün varsayılanlarıyla oluşturur.
- `Database`: host, port, veritabanı, kullanıcı, parola, TLS ve bağlantı zaman aşımıdır.

Veritabanı bilgileri veritabanının içinden okunamayacağı için bu alanların yerel kalması zorunludur. Örnek dosya `examples/HsGoStats.json` içindedir. Parola hiçbir zaman `hsgo_plugin_settings` tablosuna yazılmaz.

## Veritabanı ayarları

Oyun davranışının doğruluk kaynağı `hsgo_plugin_settings` tablosudur. Her `server_id` için şu bölüm satırları bulunur:

`enabled`, `season`, `scoring`, `game_xp`, `rating`, `weekly_missions`, `chat_commands`, `tracking`, `bans`, `persistence`, `logging`.

`setting_json` ilgili bölümün JSON değeridir. Plugin ilk çalışmada eksik satırları oluşturur; var olan satırların üzerine yazmaz. Admin paneli güncellemede `revision=revision+1`, `updated_by` ve `updated_at` alanlarını birlikte değiştirmelidir. Güvenli optimistic-lock örneği `sql/example_queries.sql` içindedir.

1.2.0'dan yükseltmede eski yerel JSON içindeki çalışma bölümleri yalnız eksik DB satırlarını ilk kez tohumlamak için okunur. Satırlar oluştuktan sonra DB daima yetkilidir; eski bölümler yerel dosyadan kaldırılabilir.

Plugin revizyonları yaklaşık 30 saniyede bir denetler. Geçerli değişiklikler canlı uygulanır. Hatalı JSON veya doğrulamadan geçmeyen bir bölüm reddedilir ve son geçerli ayar çalışmaya devam eder. Queue kapasitesi, spool ve batch gibi `persistence` değişiklikleri ile kaldırılmış eski komut alias'ları plugin/server reload sonrasında tamamen uygulanır; yeni alias'lar canlı eklenir.

## Bölüm kuralları

- `season`: `Enabled`, `Timezone`, `PreseasonTracking`, `Schedule` ve leaderboard blacklist içerir. Her sezon `Name`, açık ofsetli `StartsAt` ve `EndsAt` taşır; liste kronolojik ve çakışmasız olmalıdır. Aralık `[StartsAt, EndsAt)` biçimindedir. Tamamlanmış sezonlar aynı sırada tutulmalıdır.
- `scoring`: legacy score sayaçlarının kill, assist, death, C4 ve raund değerleridir. C4 drop/pickup uygulanmaz.
- `game_xp`: HSR olay ekonomisidir. Özel kill ödülleri toplanmaz; en yüksek eşleşen değer seçilir.
- `rating`: minimum HSR, demotion davranışı ve sıralı tier eşikleri/adları/HEX renkleridir.
- `weekly_missions`: aktif sezonla sınırlı rotasyon, ödül hedefleri, dışlanan silahlar ve 100 görevlik katalogdur. İlk/son görev haftası sezon sınırında kırpılır.
- `chat_commands`: `!sezon`, `!gorevler`, özel yanıt ve raund başı hatırlatmasıdır.
- `tracking`: istatistik olayları, warmup/bot/team-kill filtreleri ve map/weapon boyutlarıdır.
- `bans`: yalnız arşiv yetenekleridir; import ve enforcement desteklenmez.
- `persistence`: bounded queue, batch, retry/backoff ve dayanıklı spool ayarlarıdır.
- `logging`: log seviyesi ile oyuncu adının saklanıp saklanmayacağını belirler.

Puan katsayısı değişikliği geçmiş skorları yeniden hesaplamaz. Haftalık görev tanımı atama anında dondurulur. Aktif sezonun başlangıcı geriye dönük taşınmaz; tamamlanmış sezonun DB geçmişi korunur.
