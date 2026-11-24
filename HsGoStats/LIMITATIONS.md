# Limitations

- Prooyun paneli, gerçek MySQL, Linux CS2, kurulu plugin listesi ve `hsgo.tr` dış DB erişimi yoktu: **NOT TESTED**.
- Prooyun MySQL/MariaDB sürümü bilinmiyor; generated column/CHECK/`DATETIME(6)` DDL uyumluluğu staging DB'de doğrulanmalıdır.
- Hedef sunucunun CounterStrikeSharp sürümü bilinmiyor. Paket API 1.0.379/.NET 10 gerektirir; güncel upstream 1.0.380 ile bir patch farkı vardır.
- Gerçek iki oyunculu kill/assist/headshot, map change, disconnect, crash replay ve WeaponPaints/Prooyun birlikte çalışma testi yapılamadı: **NOT TESTED**.
- C4 drop/pickup puanı ve ilerlemesi kullanıcı kararıyla bu sürümün kapsamı dışındadır. Plant, kendi bombasının patlaması ve defuse uygulanır.
- Flash, molotof/incendiary ayrımı, C4 planter kimliği ve 1v2 clutch olay sırası gerçek Linux CS2 sunucusunda staging testi gerektirir.
- Round played yalnız round başlangıcında T/CT olanları içerir. Ortada katılan oyuncu bir sonraki round'a kadar sayılmaz; spectator sayılmaz.
- Playtime 10 saniyelik örneklenir, yalnız T/CT süresi sayılır ve tek örnek en çok 60 saniyedir. Normal disconnect/map/unload flush edilir; süreç event kuyruğuna girmeden aniden ölürse son örnek aralığı kaybolabilir.
- Spool event consumer'a ulaştıktan sonra dayanıklıdır. Channel'a kabul ile consumer'ın diske append'i arasındaki çok dar crash penceresi için tam WAL garantisi yoktur.
- Queue taşmasında oyun thread'i bloklanmaz; event düşürülür ve sayılı error log oluşur.
- Corrupt spool satırları atlanır; otomatik veri tahmini yapılmaz.
- `hsgo_ingested_events` retention otomatik değildir.
- Native/başka plugin ban import ve enforcement **NOT SUPPORTED / NOT TESTED**.
