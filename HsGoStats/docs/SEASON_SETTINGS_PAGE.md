# Sezon Ayarları sayfası

Bu doküman yalnız mevcut web panelindeki boş **Sezon Ayarları** sayfasının uygulanmasını tarif eder.

## Veri kaynağı

Sayfa `hsgo_plugin_settings` tablosundaki şu satırı kullanır:

```text
server_id   = seçili sunucunun ServerId değeri
setting_key = season
```

Okuma sorgusu:

```sql
SELECT setting_json, revision, updated_by, updated_at
FROM hsgo_plugin_settings
WHERE server_id = ?
  AND setting_key = 'season'
LIMIT 1;
```

`setting_json` örneği:

```json
{
  "enabled": true,
  "timezone": "Europe/Istanbul",
  "preseasonTracking": true,
  "schedule": [
    {
      "name": "Kış Sezonu",
      "startsAt": "2026-12-01T00:00:00+03:00",
      "endsAt": "2026-12-31T23:59:59+03:00"
    }
  ],
  "leaderboardBlacklistSteamIds": []
}
```

## Sayfadaki alanlar

Üst ayarlar:

- `enabled`: Sezon sistemini aç/kapat anahtarı.
- `timezone`: Varsayılan `Europe/Istanbul`.
- `preseasonTracking`: Aktif sezon yokken genel istatistik ve genel HSR ilerlesin mi?
- `leaderboardBlacklistSteamIds`: Sezon leaderboard'undan çıkarılacak SteamID64 listesi.

Sezon listesi tablosu:

| Alan | Açıklama |
|---|---|
| Sezon adı | `name` |
| Başlangıç | `startsAt` |
| Bitiş | `endsAt` |
| Durum | Planlandı / Aktif / Tamamlandı |
| İşlem | Düzenle / Sil |

Sayfada ayrıca **Yeni sezon ekle** butonu bulunmalıdır.

## Sezon durumunu gösterme

Durum DB'ye ayrıca yazılmaz; tarih aralığından hesaplanır:

```text
now < startsAt                → Planlandı
startsAt <= now < endsAt      → Aktif
now >= endsAt                 → Tamamlandı
```

Sezon bitiş zamanı dahil değildir. Plugin `[startsAt, endsAt)` aralığını kullanır.

## Yeni sezon formu

Form alanları:

- Sezon adı
- Başlangıç tarihi ve saati
- Bitiş tarihi ve saati

Backend'e gönderilen tarihler açık ofset taşımalıdır:

```text
2026-12-01T00:00:00+03:00
```

`2026-12-01T00:00:00` gibi ofsetsiz değer gönderilmemelidir.

## Doğrulama kuralları

Kaydetmeden önce hem frontend hem backend şu kontrolleri yapmalıdır:

- Sezon adı boş olamaz ve en fazla 128 karakter olabilir.
- Sezon adları büyük/küçük harf duyarsız benzersiz olmalıdır.
- `startsAt` ve `endsAt` geçerli ISO-8601 ve açık ofsetli olmalıdır.
- `endsAt`, `startsAt` değerinden sonra olmalıdır.
- Sezonlar başlangıç tarihine göre kronolojik sıralanmalıdır.
- İki sezonun tarih aralıkları çakışmamalıdır.
- Sezonlar arasında boşluk olabilir.
- SteamID64 değerleri yalnız pozitif decimal string olmalıdır.

Örnek çakışma kontrolü:

```ts
const ordered = [...schedule].sort(
  (a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt),
);

for (let i = 1; i < ordered.length; i++) {
  if (Date.parse(ordered[i].startsAt) < Date.parse(ordered[i - 1].endsAt)) {
    throw new Error("Sezon tarihleri çakışıyor.");
  }
}
```

## Düzenleme ve silme davranışı

- Planlanmış sezon tamamen düzenlenebilir veya silinebilir.
- Aktif sezonun başlangıç zamanı değiştirilememelidir.
- Aktif sezonun adı ve bitiş zamanı değiştirilebilir; bitiş değişikliği için onay diyaloğu gösterilmelidir.
- Tamamlanmış sezon düzenlenmemeli veya silinmemelidir.
- Tamamlanmış sezonların listedeki sırası korunmalıdır.

Arayüz bu kısıtları butonları devre dışı bırakarak göstermelidir. Backend de aynı kontrolleri tekrar uygulamalıdır.

## API veri tipi

Frontend modeli:

```ts
type SeasonEntry = {
  name: string;
  startsAt: string;
  endsAt: string;
};

type SeasonSettings = {
  enabled: boolean;
  timezone: string;
  preseasonTracking: boolean;
  schedule: SeasonEntry[];
  leaderboardBlacklistSteamIds: string[];
};

type SeasonSettingsResponse = {
  value: SeasonSettings;
  revision: string;
  updatedBy: string | null;
  updatedAt: string;
};
```

## Kaydetme isteği

Frontend mevcut `revision` değerini kaydetme isteğine eklemelidir:

```json
{
  "expectedRevision": "3",
  "value": {
    "enabled": true,
    "timezone": "Europe/Istanbul",
    "preseasonTracking": true,
    "schedule": [],
    "leaderboardBlacklistSteamIds": []
  }
}
```

Backend güncelleme sorgusu:

```sql
UPDATE hsgo_plugin_settings
SET setting_json = ?,
    revision = revision + 1,
    updated_by = ?,
    updated_at = UTC_TIMESTAMP(6)
WHERE server_id = ?
  AND setting_key = 'season'
  AND revision = ?
  AND JSON_VALID(?);
```

Parametreler:

1. Serialize edilmiş `SeasonSettings` JSON'u
2. Admin kimliği
3. Seçili `server_id`
4. `expectedRevision`
5. Aynı serialize edilmiş JSON

`affectedRows` değeri `0` ise başka bir admin ayarı değiştirmiş olabilir. Backend `409 Conflict` dönmeli; frontend güncel veriyi yeniden yüklemeyi önermelidir.

## Kaydetme sonrası mesaj

Başarılı kayıttan sonra:

```text
Sezon ayarları kaydedildi. Plugin değişikliği yaklaşık 30 saniye içinde kontrol edecek.
```

Panel yalnız veritabanına kaydedildiğini kesin olarak bilir. Mevcut şemada pluginin değişikliği uyguladığını bildiren ayrı bir status alanı yoktur.

## Önemli davranışlar

- Aktif sezon yokken `preseasonTracking=true` ise genel ilerleme devam eder.
- Aktif sezon yokken haftalık görev atanmaz, ilerlemez ve görev ödülü verilmez.
- Haftalık görevler sezon başlangıcında başlar ve sezon bitişinde kapanır.
- Sezon haftanın ortasında başlarsa ilk görev penceresi sezon başlangıcında açılır.
- Sezon haftanın ortasında biterse son görev penceresi sezon bitişinde kapanır.
- Leaderboard blacklist genel/all-time istatistikleri silmez.

## Tamamlanma kontrol listesi

- [ ] `season` satırı API'den okunuyor.
- [ ] Form mevcut JSON değerleriyle doluyor.
- [ ] Yeni sezon eklenebiliyor.
- [ ] Planlanmış sezon düzenlenip silinebiliyor.
- [ ] Aktif/tamamlanmış sezon kısıtları uygulanıyor.
- [ ] Tarih çakışmaları reddediliyor.
- [ ] Tarihler `+03:00` veya `Z` ile kaydediliyor.
- [ ] SteamID64 listesi doğrulanıyor.
- [ ] Güncellemede revision kontrol ediliyor.
- [ ] Çakışmada `409 Conflict` gösteriliyor.
- [ ] Başarılı kayıttan sonra güncel satır yeniden okunuyor.
