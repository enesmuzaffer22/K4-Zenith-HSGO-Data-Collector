# Ban data sources and authority

`hsgo_bans` bir yayın/arşiv tablosudur; Prooyun admin sistemi fiili yaptırım kaynağı olmaya devam eder. Tabloya INSERT/UPDATE oyuncuyu banlamaz, kick atmaz veya mevcut banı kaldırmaz. Plugin admin/chat komutu ve başka plugin entegrasyonu kaydetmez.

Desteklenen yöntem `sql/ban_import_example.sql` ile izinli manuel importtur. `source + external_ban_id` benzersizliği kaynak kimliği olan tekrarları engeller. Elle üretilen her kayıt için kararlı bir `external_ban_id` kullanın. Kaldırma DELETE değil `revoked_at`, kaldıran ve sebep alanlarını günceller. Aktif tanımı: `revoked_at IS NULL AND (expires_at IS NULL OR expires_at > UTC_TIMESTAMP())`.

`AutoImportFromExistingAdminPlugin`, `TrackNativeBanEventsIfVerified` ve `ExposeBanEnforcement` 1.0.0'da **NOT SUPPORTED**; true değer config'i güvenli biçimde reddeder. Prooyun'un eski ban kaynağı ve izinleri görülmediği için **NOT TESTED**. Arşiv tablosunun boş olması ban olmadığı anlamına gelmez. İleride otomatik sync ancak belgelenmiş API/schema, açık izin, alan eşlemesi ve idempotency entegrasyon testiyle ayrı geliştirme olarak eklenebilir.
