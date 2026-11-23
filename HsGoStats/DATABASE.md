# Database

Tüm nesneler `hsgo_` prefix'indedir. Plugin başka tabloyu okumaz/yazmaz; `DROP`/`TRUNCATE` kullanmaz. UTF-8 `utf8mb4`, UTC `DATETIME(6)`, signed BIGINT sayaçlar ve atomik `INSERT ... ON DUPLICATE KEY UPDATE` kullanılır.

Ana tablolar: oyuncu kimliği (`hsgo_players`), kalıcı takvim (`hsgo_seasons`, `hsgo_season_settings`), config'ten senkronlanan sezon sıralama dışlama listesi (`hsgo_season_leaderboard_blacklist`), genel ve sezon sayaçları, global/sezon scope'lu map/weapon sayaçları, idempotency günlüğü (`hsgo_ingested_events`) ve sezondan bağımsız `hsgo_bans`.

Blacklist config'i ilgili `server_id` için dönem aralıkları halinde saklanır. Aktif sezonda yeni eklenen SteamID'nin `effective_from_season_number` değeri bir sonraki sezon olur; mevcut ve geçmiş sezon sayaçları/sıraları değişmez. Listeden çıkarma da bir sonraki sezon için `effective_until_season_number` yazar. Hiçbir geçmiş sayaç silinmez veya yeniden hesaplanmaz. Sıralama sorguları yalnız ilgili sezon numarasını kapsayan aralıkları `NOT EXISTS` ile dışlar.

`hsgo_bans.steam_id64` bilinçli olarak foreign key değildir: manuel arşiv importu, oyuncu bu sunucuda hiç gözlenmeden önce gelebilir. Alan yine BIGINT UNSIGNED ve indekslidir.

Bir normalized event önce `event_id` ile transaction içinde claim edilir. Aynı event replay edilirse `INSERT IGNORE` sıfır satır döndürür ve hiçbir sayaç yeniden artırılmaz. Global ve sezon upsert'leri aynı transaction'dadır. Olay gözlendiği anda aldığı `season_id`, batch gecikmesi/restart sonrasında değiştirilmez. Bu, uygulama retry'ları için exactly-once etki sağlar; CS2'nin aynı mantıksal olayı iki ayrı callback olarak üretmesi için evrensel exactly-once iddiası yapılmaz.

Sezon geçişi MySQL `GET_LOCK`, unique season number/start ve generated `active_guard` ile eşzamanlı instance'lara karşı korunur. Sunucu uzun süre kapalıysa eksik dönemler sırayla oluşturulur; boş dönemlere oyuncu skoru eklenmez.

## Yetki ve kurulum

İlk açılış için CREATE/ALTER gerekmeksizin yalnız CREATE TABLE ve INSERT yetkisi gerekir; DDL yoksa `sql/schema.sql` yetkili kullanıcıyla çalıştırılır. Runtime kullanıcıya en az SELECT/INSERT/UPDATE ve sezon lock kullanımı gerekir. Web için ayrı SELECT-only kullanıcı önerilir.

Şema MySQL 8.x/MariaDB'nin generated column, CHECK ve `DATETIME(6)` özelliklerini bekler. Prooyun motor/sürümü görülmediği için staging DB'de `schema.sql` doğrulaması zorunludur.

## Yedekleme/geri yükleme

Bakım penceresinde sağlayıcının snapshot özelliğini veya `mysqldump --single-transaction --routines=false --tables hsgo_*` eşdeğerini kullanın. Geri yüklemeden önce plugin'i durdurun, hedef DB'yi ayrıca yedekleyin, yalnız `hsgo_` tablolarını restore edin ve sezonlarda en fazla bir active satır olduğunu doğrulayın. Üretim komutları sağlayıcının onayı olmadan çalıştırılmamalıdır.

`hsgo_ingested_events` büyür. Retention otomatik silinmez; spool'un maksimum yeniden oynatma ufkundan daha eski satırlar ancak yedek ve bakım prosedürüyle arşivlenmelidir.
