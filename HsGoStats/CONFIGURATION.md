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
- `rating`: minimum HSR, demotion davranışı, sıralı tier eşikleri/adları/HEX renkleri ve native scoreboard ayarlarıdır. `Scoreboard.Enabled`, `SkillGroupBaseId`, `RefreshSeconds`, aktif sezon önceliği ve genel TP fallback davranışı buradan yönetilir.
- `weekly_missions`: aktif sezonla sınırlı rotasyon, ödül hedefleri, dışlanan silahlar ve 100 görevlik katalogdur. İlk/son görev haftası sezon sınırında kırpılır.
- `chat_commands`: `!sezon`, `!seviye`, `!gorevler`, `!siralama`, komut cooldown'u, özel yanıt ve raund başı hatırlatmasıdır.
- `tracking`: istatistik olayları, warmup/bot/team-kill filtreleri ve map/weapon boyutlarıdır.
- `bans`: yalnız arşiv yetenekleridir; import ve enforcement desteklenmez.
- `persistence`: bounded queue, batch, retry/backoff ve dayanıklı spool ayarlarıdır.
- `logging`: log seviyesi ile oyuncu adının saklanıp saklanmayacağını belirler.

Puan katsayısı değişikliği geçmiş skorları yeniden hesaplamaz. Haftalık görev tanımı atama anında dondurulur. Aktif sezonun başlangıcı geriye dönük taşınmaz; tamamlanmış sezonun DB geçmişi korunur.

## 1.5.0 rank ve scoreboard yükseltmesi

Mevcut kurulumda çalışma ayarlarının kaynağı DB olduğu için yalnız DLL güncellemek tier listesini değiştirmez. Önce yedek alın, `sql/migrate_1.5.0_ranks_scoreboard.sql` içindeki `@hsgo_server_id` değerini gerçek `ServerId` ile değiştirin ve SQL'i çalıştırın. Oyuncu TP bakiyeleri değişmez; yalnız `rating` ve `chat_commands` ayar JSON'ları güncellenir.

Scoreboard ikonları için `scoreboard-addon/README.md` akışını izleyin. Plugin `FollowCS2ServerGuidelines` değerini değiştirmez. Native competitive rank alanına yazmak GSLT riski taşır ve sunucu sahibinin açık kararıdır.

TAB açıldığında rankları görünür kılmak için güncel `FakeRanks-RevealAll` Metamod eklentisi ayrıca kurulmalıdır. HsGoStats 1.5.4 ve sonrasında reveal user-message göndermez. HsGoStats 1.5.5, DB'yi daha sık sorgulamadan önbellekteki competitive rank netprop değerlerini her tick doğrular ve yalnız başka bir sistem/oyun motoru alanları değiştirdiğinde yeniden uygular.

## 1.6.0 native Competitive rank yükseltmesi

`sql/migrate_1.6.0_native_competitive_ranks.sql`, `rating.tiers` listesini Silver I ile Global Elite arasındaki 18 CS2 Competitive seviyesiyle değiştirir ve `scoreboard.skillGroupBaseId=1` yapar. Yeni tablo veya kolon oluşturmaz; genel/sezon TP bakiyelerine dokunmaz. Görünen seviye mevcut TP'nin yeni eşiklere göre yeniden çözülmesiyle değişir.

1.6.0 için özel SVG Workshop addonu gerekmez. Eski addon yalnız özel `19010001+` ikonlarını içeriyorsa MultiAddonManager yükleme listesinden çıkarılabilir. MultiAddonManager başka içerikler için kullanılmaya devam edebilir. `FakeRanks-RevealAll` ise HsGoStats'ın yazdığı rankları TAB açıldığında görünür kıldığı için kurulu kalmalıdır.
