# Admin panel contract

Admin backend doğrudan `hsgo_plugin_settings` tablosunu kullanabilir. Tarayıcıdan MySQL'e bağlanılmamalı; tüm sorgular server-side ve parametreli olmalıdır.

## Ayar okuma ve yazma

Bir ayarı düzenlerken panel önce `setting_json` ve `revision` değerini okur. Kaydederken geçerli JSON üretir ve şu optimistic-lock koşulunu uygular:

```sql
UPDATE hsgo_plugin_settings
SET setting_json=:setting_json,
    revision=revision+1,
    updated_by=:admin_identity,
    updated_at=UTC_TIMESTAMP(6)
WHERE server_id=:server_id
  AND setting_key=:setting_key
  AND revision=:expected_revision
  AND JSON_VALID(:setting_json);
```

Etkilenen satır sayısı sıfırsa kayıt başka bir admin tarafından değiştirilmiş, JSON geçersiz veya anahtar bulunamamıştır. Panel güncel satırı yeniden okuyup kullanıcıya çakışma göstermelidir. `Database` parolası bu tabloya veya web istemcisine taşınmamalıdır.

## Son 24 saat aktivitesi

`hsgo_player_sessions` her bağlantı için başlangıç, son heartbeat, kapanış ve süreyi tutar. `sql/example_queries.sql` tek sorguda şunları döndürür:

- `unique_players_24h`: son 24 saatin herhangi bir anında bağlı olan benzersiz insan oyuncular.
- `sessions_started_24h`: son 24 saatte başlayan bağlantı oturumları.
- `average_session_seconds_24h`: bu aralıkta başlayan oturumların ortalama geçen süresi.

Aktif oturumlarda süre, en son heartbeat'e kadar hesaplanır. Plugin beklenmedik biçimde kapanırsa bir sonraki başlangıç açık oturumu son heartbeat zamanında kapatır; sunucu kapalı kaldığı süre oyuncu süresine eklenmez.
