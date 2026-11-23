# Web integration

Next.js/backend yalnız server-side route, server action veya ayrı backend servisinden MySQL'e bağlanmalıdır. Kimlik bilgilerini `NEXT_PUBLIC_*`, tarayıcı bundle'ı veya HTML içine koymayın. Ban reason/name gibi metinleri çıktı kodlamasıyla escape edin; SQL parametreleri kullanın.

Örnek sorgular `sql/example_queries.sql` içindedir: sezon listesi/aktif sezon, profil, sezon ve all-time score top 10, kills top 10, K/D (`kills/GREATEST(1,deaths)`), HS%, playtime, map/weapon ve aktif/geçmiş banlar. Sezon leaderboard sorguları `hsgo_season_leaderboard_blacklist` aralığını sezon numarasıyla karşılaştırmalıdır; böylece sonradan eklenen oyuncu geçmiş sezonlardan silinmez, yalnız `effective_from_season_number` ve sonrasında dışlanır. All-time sorguları blacklist uygulamaz. Son sezon satırını aktif varsaymayın; `status` ve `[starts_at_utc, ends_at_utc)` aralığını birlikte kullanın. Bootstrap sonrasında DB takvimin tek doğruluk kaynağıdır.

Önerilen web hesabı yalnız `SELECT` yetkili ve IP allowlist'li olmalıdır. Oyun sunucusundan DB yazma erişimi ile `hsgo.tr` backend'inden dış erişim ayrı ayrı test edilmelidir. Prooyun dış bağlantıyı kapatıyorsa tarayıcıdan doğrudan DB bağlantısı kurmayın; sağlayıcıdan izinli read-only erişim veya kullanıcı onaylı API/senkronizasyon çözümü isteyin. Bu teslimat transfer hizmeti kurmaz.
