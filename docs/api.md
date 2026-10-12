# API sözleşmesi

Yerel adres `http://localhost:5248`; OpenAPI `/openapi/v1.json` (geliştirme). Giriş ve token kuralları
`docs/security.md`'dedir. Bu belge API-15'te örnek istek/cevaplarla genişletilecek.

## Genel JSON kuralları (API-03, 12 Ekim 2026, kullanıcı kararları)

- **Enum'lar yazıyla taşınır:** `"accountType": "Personal"`, ileride `"kind": "Purchase"`. Girdi olarak da
  yalnız ad kabul edilir; `2` gibi bir sayı 400'dür (sessiz dönüşüm yok). Gerekçe: insanlar ve LLM için
  okunur; modelin `2`'nin ne olduğunu tahmin etmesi gerekmez.
- **Para JSON sayısıdır:** `"balance": 577490.18`. `decimal` birebir yazılır (`2595.00` → `2595.00`).
  Bütün hesaplar backend'de yapılır; istemciler tutarı yalnız gösterir. Swift'in varsayılan JSON okuyucusu
  `Decimal`'i `double` üzerinden okuyabildiği için SwiftUI adımında tutar dikkatli çözülecek.
- **Yazma isteklerinde tanınmayan alan 400'dür** (`[JsonUnmappedMemberHandling(Disallow)]`). Örneğin hesap
  açarken `balance` göndermek sessizce yok sayılmaz, reddedilir. Okuma istekleri ve login bu kurala tabi değil.
- Alan adları camelCase'dir (ASP.NET varsayılanı).

## Hesap (API-03)

`AccountDto` (Application/Accounts) — dışarı giden hesap:

```json
{
  "id": "20000000-0000-0000-0000-000000000001",
  "accountName": "Ada günlük TRY",
  "accountNumber": "DUMMY-ADA-TRY-001",
  "currencyCode": "TRY",
  "accountType": "Personal",
  "balance": 577490.18,
  "createdAt": "2026-10-09T14:08:38.294505+00:00"
}
```

- `customerId` yoktur: sahip her zaman token'daki müşteridir.
- `id` vardır: istemci hesabı seçmek için kullanır; sahiplik her istekte backend'de kontrol edilir
  (başka müşterinin hesabı 404).
- `AccountDto.Projection` EF Core'un SQL'e çevirdiği eşlemedir; yalnız bu kolonlar okunur (`CustomerId`
  okunmaz). Sıralama `Select`'ten önce uygulanmalıdır.

`CreateAccountRequest` (Api) — `POST /api/accounts` gövdesi (endpoint API-07'de):

```json
{ "accountName": "Tatil birikimi", "currencyCode": "EUR" }
```

| Alan | Kural |
|---|---|
| `accountName` | zorunlu, 1–100 karakter, yalnız boşluk olamaz |
| `currencyCode` | zorunlu, tam olarak `TRY`/`USD`/`EUR`/`GBP` (`try` düzeltilmez, 400) |

Backend belirler: sahip (token), `id`, `createdAt`, `accountNumber` (biçimi API-07), `accountType`
(her zaman `Personal`; `Business` yalnız demo dükkânı), `balance` (her zaman 0; para yalnız işlemle girer).

Bilinen sınır: `[StringLength(100)]` UTF-16 birimi sayar, DB karakter sayar; emoji gibi iki birimlik
karakterlerde API sınırı biraz daha sıkıdır.

## Hesap okuma servisi (API-04)

`IAccountReadService` (Application/Accounts) — controller'lar bu sözleşmeye bağlanır (kullanıcı kararı);
gerçekleştirimi `AccountReadService`.

| Metot | Sonuç | Hata |
|---|---|---|
| `ListAsync(customerId)` | müşterinin bütün hesapları; hesabı yoksa boş liste | bilinmeyen müşteri `KeyNotFoundException`, boş kimlik `ArgumentException` |
| `GetAsync(customerId, accountId)` | tek hesap | olmayan **veya başka müşterinin** hesabı aynı `KeyNotFoundException` (varlık sızmaz) |

- Sahiplik `CustomerScope`'ta karar verilir (analytics ile aynı kural, tek yer).
- Sıra: açılış tarihi (eskiden yeniye), eşitse hesap numarası (demo hesapları aynı anda açıldı).
- Sorgular: liste 2 (müşteri + hesaplar), tek hesap 3 (müşteri + sahiplik + DTO).
- Repository yoktur; `BankingDbContext` bu rolü görür. DTO projeksiyonu EF tarafından takip edilmez.
- HTTP eşlemesi API-05/06'da: `KeyNotFoundException` → 404.

## `GET /api/accounts` (API-05)

Token gerekir. Müşteri yalnız token'dan alınır; `?customerId=` gibi bir parametre etkisizdir.

| Durum | Cevap |
|---|---|
| Geçerli token | `200` + hesapların **düz JSON dizisi** (açılış tarihi, eşitse hesap numarası sırasıyla) |
| Hesabı olmayan müşteri | `200` + `[]` |
| Token yok / geçersiz / süresi dolmuş | `401` |
| Token geçerli ama müşteri artık yok | `401` (`/api/me` ile aynı; istemci yeniden giriş ister) |

Sayfalama ve istemci tarafı sıralama parametresi yoktur (kullanıcı kararı): bir müşterinin hesap sayısı
küçüktür; sayfalama işlem listesinde (API-09) tasarlanacak. Örnek (Ada):

```json
[
  { "id": "20000000-0000-0000-0000-000000000005", "accountName": "Ada boş TRY", "accountNumber": "DUMMY-ADA-EMPTY-001", "currencyCode": "TRY", "accountType": "Personal", "balance": 0.00, "createdAt": "2026-10-09T14:08:38.294505+00:00" },
  { "id": "20000000-0000-0000-0000-000000000001", "accountName": "Ada günlük TRY", "accountNumber": "DUMMY-ADA-TRY-001", "currencyCode": "TRY", "accountType": "Personal", "balance": 577490.18, "createdAt": "2026-10-09T14:08:38.294505+00:00" },
  { "id": "20000000-0000-0000-0000-000000000002", "accountName": "Ada USD", "accountNumber": "DUMMY-ADA-USD-001", "currencyCode": "USD", "accountType": "Personal", "balance": 500.00, "createdAt": "2026-10-09T14:08:38.294505+00:00" }
]
```

Bilinen sınır: OpenAPI belgesi şimdilik yalnız `200`'ü gösterir; `401` API-15'te belgelenecek.

## `GET /api/accounts/{id}` (API-06)

Token gerekir. Hesap yalnız token'daki müşterinin hesaplarında aranır.

| Durum | Cevap |
|---|---|
| Kendi hesabı | `200` + tek `AccountDto` |
| Olmayan hesap (sıfır GUID dahil) | `404` |
| **Başka müşterinin hesabı** | `404`, olmayan hesapla birebir aynı gövde (403 verilmez; varlık sızmaz) |
| Kimlik GUID değil (`abc`, `123`) | `400` (ASP.NET doğrulama cevabı; kullanıcı kararı) |
| Token yok / geçersiz | `401` |
| Token geçerli ama müşteri artık yok | şimdilik `404` (servis müşteri ve hesap yokluğunu aynı istisnayla bildiriyor; API-13'te `401` olacak) |

404 gövdesi (ASP.NET varsayılanı; ortak biçim API-13'te):

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "traceId": "…" }
```

## `POST /api/accounts` (API-07)

Token gerekir. Gövde `CreateAccountRequest` (yukarıda): yalnız `accountName` ve `currencyCode`.

```http
POST /api/accounts
{ "accountName": "Tatil birikimi", "currencyCode": "EUR" }

201 Created
Location: /api/accounts/c7f08208-4546-4b40-8b48-e83c50be4ab0
{ "id": "c7f08208-…", "accountName": "Tatil birikimi", "accountNumber": "ACC-91", "currencyCode": "EUR",
  "accountType": "Personal", "balance": 0.00, "createdAt": "2026-10-12T12:28:53.563327+00:00" }
```

| Durum | Cevap |
|---|---|
| Geçerli istek | `201` + `Location` + yeni hesap (`GET /api/accounts/{id}` ile birebir aynı) |
| Boş/yalnız boşluk/101+ karakter ad, para birimi yok/geçersiz (`try`, `JPY`), boş gövde | `400` |
| Tanınmayan alan (`balance`, `customerId`, `accountType`…) | `400` (sessizce yok sayılmaz) |
| Hesap kurallarına takılan ad (ör. NUL karakteri) | `400` |
| Token yok / geçersiz | `401` |
| Token geçerli ama müşteri artık yok | `401` |

- Backend belirler: tür `Personal`, bakiye `0`, `createdAt` sunucu saati, sahip token'daki müşteri.
- Numara: PostgreSQL `AccountNumberSequence` sayacından `ACC-n` (kullanıcı kararı). Aynı anda gelen
  istekler bile aynı numarayı alamaz; sayaç geri alınmadığı için numaralarda boşluk olabilir.
- Ad olduğu gibi saklanır (kırpılmaz); aynı adla birden çok hesap açılabilir.

Bilinen sınırlar: müşteri başına hesap sayısı sınırı yok (kullanıcı kararı; ACC-LIMIT); aynı isteğin
iki kez gönderilmesi iki hesap açar (API-12); 400 cevabı tanınmayan alanda iç tip adını içeriyor (API-13).

