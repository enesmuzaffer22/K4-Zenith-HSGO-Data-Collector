# Source audit

## Doğrulanan kaynak durumu

23 Kasım 2026'da `upstream=https://github.com/K4ryuu/K4-Zenith.git` için `git ls-remote` sonucu `HEAD`, `dev` ve `v1.0.41` etiketinin tamamında `94cd8fb34ffa2121d07ec7846dbcb9c1ac37935e` gösterdi. Yerel checkout aynı commit ve `dev` branch'indedir. K4-Zenith GPL-3.0 lisanslıdır.

İncelenen ana alanlar:

- `modules/statistics/K4-Zenith-Stats.cs`: event kayıtları, kill/assist/headshot, hurt, round, bomb, map/weapon mantığı.
- `modules/statistics/K4-Zenith-Stats.csproj`: .NET 8, CounterStrikeSharp, Dapper, MySqlConnector, ZenithAPI ve KitsuneMenu bağımlılıkları.
- `modules/time-stats/K4-Zenith-TimeStats.cs`: on saniyelik oyun süresi örnekleme yaklaşımı.
- `src/Models/Database/Migrations/Stats/1.0_Zenith_Stats_CreateTables.cs`: eski map/weapon şeması.
- `src/Models/Database/Model.cs`, `src/Models/Player/Storage.cs`: bağlantı ve Zenith JSON depolama modeli.
- `modules/zenith-bans/*` ve `src/Models/Database/Migrations/Bans/*`: admin/enforcement kapsamının stats arşivinden neden ayrılması gerektiği.

## Dependency graph ve karar

Orijinal Stats modülü `ZenithAPI` capability'leri → Zenith Core config/storage → Dapper/MySqlConnector ve KitsuneMenu/komut/mesaj katmanına bağlıdır. Bu graph, tek başına çalışma ve oyuncuya görünmezlik şartıyla uyumsuzdur.

Bu teslimatta K4 kaynak dosyası doğrudan kopyalanmadı. Yalnız doğrulanan event kapsamı ve veri alanları davranışsal referans olarak kullanıldı; queue, sezon, puanlama, schema, spool ve plugin giriş noktası sıfırdan yazıldı. Dolayısıyla satır bazlı taşınmış K4 kodu yoktur. Proje yine de kaynak kökenini açıkça belirtir ve GPL-3.0 altında dağıtılır.

## Bilinçli olarak taşınmayanlar

Zenith storage JSON'u, `IZenithEvents`, module capability'leri, komutlar, menüler, localization, placeholder'lar, minimum oyuncu mesajları, admin sistemi, K4 Bans enforcement ve reset/TRUNCATE işlemleri taşınmadı. Dapper/FluentMigrator da gerekli olmadığı için kullanılmadı.

CounterStrikeSharp tarafında resmi `BasePlugin`, `IPluginConfig<T>`, game event handler ve listener API'leri kullanıldı. Güncel resmi release 1.0.380 görünse de yerel doğrulanmış NuGet/API 1.0.379 ile temiz build alındı; hedef sunucu sürümü bilinmediğinden bu fark `LIMITATIONS.md` içinde işletim kontrolü olarak tutuldu.
