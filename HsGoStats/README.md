# HSGo Stats Collector 1.0.1

Tek `BasePlugin` girişli, oyuncuya görünmeyen CounterStrikeSharp istatistik toplayıcısıdır. Zenith, menü, admin, rank veya başka bir oyun eklentisine ihtiyaç duymaz; yalnız CounterStrikeSharp ve paket içindeki `MySqlConnector` kullanılır. Chat/admin komutu, HUD, clan tag, skor tablosu, skin, ConVar veya ban enforcement eklemez.

## Uyumluluk

Bu sürüm 23 Kasım 2026 tarihinde CounterStrikeSharp API `1.0.379` ve `.NET 10` ile derlenmiştir (`MinimumApiVersion(379)`). Prooyun sunucusundaki gerçek CounterStrikeSharp sürümü görülmediği için kurulumdan önce `css_plugins list`/sunucu konsolu ile en az 1.0.379 ve .NET 10 runtime doğrulanmalıdır. Daha eski bir sunucu için API hedefi bilinçsizce düşürülmemelidir.

## Kurulum

1. `publish/HsGoStats` içeriğini `csgo/addons/counterstrikesharp/plugins/HsGoStats/` altına kopyalayın.
2. `examples/HsGoStats.json` dosyasını `csgo/addons/counterstrikesharp/configs/plugins/HsGoStats/HsGoStats.json` olarak kopyalayın.
3. Prooyun panelindeki gerçek MySQL host, port, veritabanı, kullanıcı ve parolayı yazın. `CHANGE_ME` ile plugin güvenli biçimde devre dışı kalır.
4. Sunucuyu yeniden başlatın. Konsolda `HSGo Stats is ready` mesajını ve aktif sezonu kontrol edin.
5. DB kullanıcısında DDL yetkisi yoksa bir yönetici `sql/schema.sql` dosyasını bir kez çalıştırmalıdır.

Doğal CounterStrikeSharp config yolu kullanılır. Parola loglanmaz. `data/pending-events.jsonl` plugin klasöründe otomatik oluşan crash-recovery spool dosyasıdır.

## VS Code / .NET CLI ile derleme

Visual Studio gerekmez. .NET 10 SDK ile repo kökünden:

```powershell
dotnet restore HsGoStats/HsGoStats.slnx
dotnet build HsGoStats/HsGoStats.slnx -c Release --no-restore
dotnet test HsGoStats/tests/HsGoStats.Tests.csproj -c Release --no-build
```

Release çıktısı `src/bin/Release/net10.0` altındadır. Paket yeniden üretmek için temiz build sonrasında `HsGoStats.dll`, `MySqlConnector.dll` ve MySqlConnector'ın özel bağımlılıklarını `publish/HsGoStats` içine kopyalayın; CounterStrikeSharp API DLL'sini kopyalamayın çünkü sunucu sağlar.

## Davranış özeti

- Kill/death/assist/headshot, gerçek oyuncu hasarı, round, skor, playtime, harita ve silah sayaçları.
- Genel ve sezonluk satırlar aynı idempotent transaction içinde ayrı artırılır.
- `Season.LeaderboardBlacklistSteamIds` oyuncuları genel istatistiklerde tutar; aktif/geçmiş sezonu değiştirmeden bir sonraki sezondan itibaren sezonluk hedeflerden çıkarır.
- 30×24 saatlik sabit UTC dönemleri; geçmiş DB takvimi config değişikliğiyle kaydırılmaz.
- `event_id` + `hsgo_ingested_events` tekrar oynatmada çift artırmayı engeller.
- Bounded queue, exponential retry ve isteğe bağlı disk spool oyun thread'ini DB/disk I/O'dan ayırır.
- Ban tablosu yalnız arşivdir; SQL kaydı oyuncuyu banlamaz.

Ayrıntılar için `CONFIGURATION.md`, `DATABASE.md`, `LIMITATIONS.md` ve `TEST_REPORT.md` dosyalarına bakın.
