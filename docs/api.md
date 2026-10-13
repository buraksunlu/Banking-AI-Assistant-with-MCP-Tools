# API sözleşmesi

Yerel adres `http://localhost:5248`; OpenAPI `/openapi/v1.json` (geliştirme). Giriş ve token kuralları
`docs/security.md`'dedir. Bu belge API-15'te örnek istek/cevaplarla genişletilecek.

## Endpoint özeti (13 Ekim 2026)

| Endpoint | Token | Durum | Madde |
|---|---|---|---|
| `GET /api/health` | hayır | uygulandı | API-01b |
| `POST /api/auth/login` | hayır | uygulandı | SEC-01b |
| `GET /api/me` | evet | uygulandı | SEC-01b |
| `GET /api/accounts` | evet | uygulandı | API-05 |
| `GET /api/accounts/{id}` | evet | uygulandı | API-06 |
| `POST /api/accounts` | evet | uygulandı | API-07 |
| `GET /api/accounts/{accountId}/transactions` | evet | uygulandı | API-09 |
| `GET /api/transactions/{id}` | evet | uygulandı | API-10 |
| `POST /api/accounts/{accountId}/transactions` | evet | uygulandı | API-11 |
| `GET /api/analytics/…` | evet | planlı | ANA-09 |

Ortak davranış: müşteri yalnız token'dan gelir (istekte müşteri parametresi yoktur); başka müşterinin
kaynağı olmayan kaynakla birebir aynı `404`'ü alır; GUID olmayan kimlik `400`'dür.

## Genel JSON kuralları (API-03, 12 Ekim 2026, kullanıcı kararları)

- **Enum'lar yazıyla taşınır:** `"accountType": "Personal"`, `"kind": "Purchase"`. Girdi olarak yalnız
  **tam ad** kabul edilir; `2` gibi bir sayı ve `"purchase"` gibi farklı harf büyüklüğü 400'dür (API-13, kullanıcı
  kararı 4-A; `?period=lastmonth` da). Gerekçe: insanlar ve LLM için okunur; sessiz dönüşüm yok.
- **Para JSON sayısıdır:** `"balance": 577490.18`. `decimal` birebir yazılır (`2595.00` → `2595.00`).
  Bütün hesaplar backend'de yapılır; istemciler tutarı yalnız gösterir. Swift'in varsayılan JSON okuyucusu
  `Decimal`'i `double` üzerinden okuyabildiği için SwiftUI adımında tutar dikkatli çözülecek.
- **Yazma isteklerinde tanınmayan alan 400'dür** (`[JsonUnmappedMemberHandling(Disallow)]`). Örneğin hesap
  açarken `balance` göndermek sessizce yok sayılmaz, reddedilir. Okuma istekleri ve login bu kurala tabi değil.
- Alan adları camelCase'dir (ASP.NET varsayılanı).

## Hata cevapları (API-13, 13 Ekim 2026, kullanıcı kararları)

Her hata aynı biçimdedir: **ProblemDetails** (RFC 9457), içerik tipi `application/problem+json`, Türkçe `title`,
sabit makine kodu `code` ve `traceId`. Gövdesiz cevap yoktur (401, bilinmeyen adres 404, 405, 429 dahil). İstemci
başlığı ayrıştırmak yerine `code`'a bakar.

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "İstek geçersiz.", "status": 400,
  "errors": { "balance": ["Tanınmayan alan."] }, "traceId": "…", "code": "validation_failed" }
```

| Durum | Kod | `code` | `title` |
|---|---|---|---|
| Girdi kuralı, bozuk JSON, GUID olmayan kimlik, tanınmayan alan | 400 | `validation_failed` | İstek geçersiz. (+ alan bazlı `errors`) |
| Token yok / geçersiz / süresi dolmuş; yanlış kullanıcı adı veya şifre | 401 | `unauthorized` | Giriş gerekli. |
| Token geçerli ama müşteri artık yok | 401 | `session_invalid` | Oturum geçersiz; yeniden giriş yapın. |
| Olmayan veya başkasının kaynağı; bilinmeyen adres | 404 | `not_found` | Bulunamadı. (`detail` yok; gövdeler birebir aynı) |
| Yanlış HTTP metodu | 405 | `method_not_allowed` | Bu adres bu yöntemi desteklemiyor. |
| Bakiye sınırı (yetersiz bakiye, üst sınır) | 409 | `balance_limit` | Bakiye sınırı (+ `detail`) |
| JSON olmayan gövde | 415 | `unsupported_media_type` | Desteklenmeyen içerik türü; JSON gönderin. |
| Login deneme sınırı | 429 | `too_many_requests` | Çok fazla deneme; biraz sonra tekrar deneyin. |
| Beklenmeyen hata | 500 | `internal_error` | Beklenmeyen bir hata oluştu. (ayrıntı yalnız sunucu logunda) |

`errors` kuralları:

- Anahtar, istemcinin gönderdiği addır (camelCase JSON/sorgu adı: `accountName`, `limit`, `password`); gövdenin
  bütünü için `body`.
- Mesajlar Türkçedir; .NET'in `(Parameter '…')` eki, iç tip adları, JSON yolları (`$.x`) ve bayt konumları görünmez.
  JSON hataları sabit metne çevrilir: tanımlı olmayan alan → "Tanınmayan alan.", tipi tutmayan değer → "Geçersiz
  değer.", ayrıştırılamayan gövde → "Geçersiz JSON.", boş gövde → "İstek gövdesi zorunlu.".
- İstekler metin ve tutar kurallarını `InputRules`'tan yeniden kullanır; NUL karakter de istemcinin alanında
  (`description`, `accountName`) raporlanır, karakterler DB gibi sayılır.

Kullanıcı kararları: her yerde ProblemDetails (1-A); eşleme tek yerde, `ApiExceptionHandler` + `UseStatusCodePages`,
controller'larda `try/catch` yok (2-A); Türkçe mesajlar (3-A); katı enum (4-A); her ortamda aynı genel 500, geliştirme
ortamında da stack trace yok (5-A); `code` alanı (6-A). Silinmiş müşteri her endpoint'te 401 (API-06 kararı).

Bilinen sınır: servislerden gelen her `ArgumentException` 400 sayılır; programlama hatasından doğan bir
`ArgumentException` da 400 görünür (mesajlar bizim Türkçe kurallarımızdır; ayrı tip `InputRules`'u ve 70 testi
etkilerdi).

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

`CreateAccountRequest` (Api) — `POST /api/accounts` gövdesi (API-07):

```json
{ "accountName": "Tatil birikimi", "currencyCode": "EUR" }
```

| Alan | Kural |
|---|---|
| `accountName` | zorunlu, 1–100 karakter, yalnız boşluk olamaz |
| `currencyCode` | zorunlu, tam olarak `TRY`/`USD`/`EUR`/`GBP` (`try` düzeltilmez, 400) |

Backend belirler: sahip (token), `id`, `createdAt`, `accountNumber` (`ACC-n`, API-07), `accountType`
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
- HTTP eşlemesi (API-05/06): `KeyNotFoundException` → 404 (listede müşteri yoksa 401).

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
| Token geçerli ama müşteri artık yok | `401` `session_invalid` (API-13) |

404 gövdesi (ortak biçim, yukarıda):

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Bulunamadı.", "status": 404, "traceId": "…", "code": "not_found" }
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
iki kez gönderilmesi iki hesap açar (API-12).

## İşlem sözleşmesi (API-08)

Liste (API-09), tek işlem (API-10) ve ekleme (API-11) uygulandı.

### Dışarı giden işlem: `TransactionItem`

Kullanıcı kararı: API ayrı bir işlem DTO'su yazmaz; analytics'in döndürdüğü `TransactionItem` kullanılır
(maskeleme ve join'ler tek yerde, `TransactionItemReader`). API-08 ile `accountId` eklendi.

```json
{ "id": "30000000-0000-0000-0000-000000000602", "accountId": "20000000-0000-0000-0000-000000000001",
  "accountName": "Ada günlük TRY", "occurredAtUtc": "2026-10-06T09:00:00+00:00", "localDate": "2026-10-06",
  "kind": "Purchase", "direction": "Debit", "amount": 210.00, "currencyCode": "TRY", "merchant": "Starbucks",
  "categoryCode": "FOOD", "categoryDisplayName": "Yeme & İçme", "rawDescription": "POS 4412 STARBUCKS KADIKOY",
  "counterparty": null, "counterpartyIsOwnAccount": false }
```

- Transferde `counterparty` maskelidir (`"Deniz M***"`); kendi hesabına transferde hesap adı ve
  `counterpartyIsOwnAccount: true`; dükkân alışverişinde dükkân hesabının adı.
- Çıkmayan iç alanlar: `pairId`, kategorizasyon kaynağı/güveni/zamanı, `createdAt`, müşteri kimliği.

### İçeri gelen: `CreateTransactionRequest` (`POST /api/accounts/{accountId}/transactions`, API-11)

```json
{ "kind": "Purchase", "amount": 210.00, "description": "POS 4412 STARBUCKS KADIKOY", "merchant": "Starbucks" }
```

| Alan | Kural |
|---|---|
| `kind` | zorunlu; yalnız `Purchase` veya `Salary` (kullanıcı kararı; `Transfer`, `Sale`, `OpeningFunding`, sayı → 400) |
| `amount` | zorunlu; 0,01 ≤ tutar ≤ 9999999999999999,99; en fazla 2 ondalık (`12.345` → 400, yuvarlanmaz) |
| `description` | zorunlu; 1–500 karakter, yalnız boşluk olamaz |
| `merchant` | isteğe bağlı; verilirse 1–150 karakter, boş/boşluk olamaz; `Salary` ile birlikte verilemez |

Backend belirler (istekte gelirse 400): `direction` (Purchase → Debit, Salary → Credit), `currencyCode`
(hesabın para birimi), `accountId` (adresten), `occurredAt` (sunucu saati; geçmiş tarih yok — kullanıcı
kararı), kategori alanları (bankanın yorumu; yeni işlem "Sınıflandırılmamış" başlar).

Enum girdisi katıdır: `"purchase"` 400'dür (API-13).

## `GET /api/accounts/{accountId}/transactions` (API-09)

Token gerekir. Hesabın işlemlerini en yeniden eskiye (eşitse işlem kimliği) sayfa sayfa döndürür. Liste,
filtre ve sahiplik `TransactionSearchService` içindedir; bütün türler (maaş, transfer, satış, harcama)
ve Business hesaplar dahildir.

| Parametre | Kural |
|---|---|
| `period` | isteğe bağlı, yalnız adla: `ThisMonth`, `LastMonth`, `Last3Months`, `Last6Months` |
| `from`, `to` | isteğe bağlı, `yyyy-MM-dd`, **birlikte** (yerel tarih, dahil; özel dönem en fazla 24 ay); `period` ile birlikte verilemez |
| (ikisi de yoksa) | **son 3 ay** (kullanıcı kararı) |
| `limit` | 1–50, varsayılan 20 |
| `offset` | 0–10.000, varsayılan 0 |

Tür/yön/kategori/merchant filtreleri bu endpoint'te yoktur (kullanıcı kararı; ANA-09'da).

```json
{
  "period": { "kind": "Custom", "startUtc": "2026-09-30T21:00:00+00:00", "endUtc": "2026-10-10T21:00:00+00:00",
              "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "totalCount": 8,
  "hasMore": true,
  "items": [ { "id": "30000000-0000-0000-0000-000000000604", "kind": "Purchase", "amount": 145.00, "...": "..." } ]
}
```

| Durum | Cevap |
|---|---|
| Kendi hesabı | `200` + sayfa (işlem yoksa `items: []`, `totalCount: 0`) |
| Başka müşterinin / olmayan hesap | `404` (birebir aynı gövde) |
| Bozuk parametre (limit/offset sınırı, tek tarih, `from` > `to`, 24 aydan uzun, `period` + tarih, `period=Custom`/`2`/`Foo`, geçersiz tarih) veya GUID olmayan kimlik | `400` |
| Token yok / geçersiz | `401` |

Bilinen sınırlar: offset sayfalamasında liste gezilirken araya yeni işlem girerse sayfa kenarında bir
işlem tekrar görünebilir (istemci kimliğe göre ayıklamalı); API gerçek saati kullanır (demo "şimdi" ANA-09);
silinmiş müşterinin token'ı `401` `session_invalid` (API-13).

## `GET /api/transactions/{id}` (API-10)

Token gerekir. Tek işlemi kimliğiyle, **zarfsız** düz `TransactionItem` olarak döndürür (liste kalemiyle birebir
aynı biçim). Okuma `TransactionSearchService.GetAsync`'tedir: bütün türler, müşterinin bütün hesapları (Business
dükkân dahil), **dönem sınırı yok** (2025 kaydı da okunur). Sahiplik aynı sorgunun içindedir (2 SQL komutu: saat
dilimi + tek join'li okuma).

| Durum | Cevap |
|---|---|
| Kendi hesabındaki işlem | `200` + `TransactionItem` |
| Olmayan işlem (sıfır GUID dahil) | `404` |
| **Başka müşterinin hesabındaki işlem** | `404`, olmayan işlemle birebir aynı gövde. Ada'nın kendi alışverişinin dükkândaki bacağı (Deniz'in hesabı) da bu durumdadır |
| Kimlik GUID değil (`abc`, `123`) | `400` (route kısıtı yok; kullanıcı kararı, API-06 ile aynı) |
| Token yok / geçersiz | `401` |
| Token geçerli ama müşteri artık yok | `401` `session_invalid` (API-13) |

Kullanıcı kararları (API-10): okuma ayrı servis değil arama servisinin metodu (analytics interface'siz kalır);
liste ve tek işlem aynı `TransactionsController`'da, her metot kendi tam route'unu taşır; adres iç içe değil
`api/transactions/{id}` (hesap kimliği gerekmez).

Bilinen sınırlar: transferin iki bacağı ayrı kimliklerdir; müşteri yalnız kendi bacağını okur (karşı taraf maskeli
adla görünür). OpenAPI şimdilik yalnız `200`'ü gösterir (API-15).

## `POST /api/accounts/{accountId}/transactions` (API-11)

Token gerekir. Müşterinin kendi hesabına **sentetik** bir Purchase (para çıkışı) veya Salary (para girişi) ekler;
işlem satırı ve bakiye değişimi tek DB transaction'ında yazılır (`TransactionWriter`). Gövde `CreateTransactionRequest`
(yukarıda). LLM/MCP'ye verilmez (RULE §15); MCP tool'ları read-only kalır.

```http
POST /api/accounts/{accountId}/transactions
{ "kind": "Purchase", "amount": 250.75, "description": "POS 4412 STARBUCKS KADIKOY", "merchant": "Starbucks" }

201 Created
Location: /api/transactions/{id}
{ "id": "…", "accountId": "…", "kind": "Purchase", "direction": "Debit", "amount": 250.75, "currencyCode": "TRY",
  "merchant": "Starbucks", "categoryCode": null, "categoryDisplayName": "Sınıflandırılmamış", "…": "…" }
```

Cevap `GET /api/transactions/{id}` ile birebir aynıdır (kayıttan sonra aynı okuma koduyla okunur).

| Durum | Cevap |
|---|---|
| Geçerli istek | `201` + `Location` + işlem |
| Gövde kuralları (`kind` yalnız Purchase/Salary, tutar, açıklama, merchant, merchant'lı Salary, tanınmayan alan), boş gövde, NUL karakter, GUID olmayan hesap kimliği | `400` |
| Token yok / geçersiz | `401` |
| Başkasının / olmayan hesap | `404` (birebir aynı gövde) |
| Token geçerli ama müşteri artık yok | `401` `session_invalid` (API-13) |
| Yetersiz bakiye; bakiye üst sınırını (9999999999999999,99) aşacak maaş | `409` |

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Bakiye sınırı", "status": 409,
  "detail": "Yetersiz bakiye nedeniyle Debit işlemi reddedildi.", "traceId": "…" }
```

Kullanıcı kararları (API-11, 13 Ekim 2026):

- Yetersiz bakiye **409**: istek geçerli, hesabın şu anki durumu izin vermiyor; aynı istek maaş girdikten sonra
  başarılı olabilir. Yazıcı bunu ayrı bir tiple (`BalanceLimitException`) bildirir, programlama hataları 500 kalır.
- **Business hesap da kabul edilir** (karar 3-B). Business hesaptaki harcamalar harcama analizlerinde görünmez
  (analizler yalnız şahsi hesaplara bakar). Demo dükkânına yazılırsa veri bütünlüğü testinin dükkân fixture'ı değişir.
- `merchant` olduğu gibi saklanır (`"starbucks"` → `"starbucks"`); sessiz düzeltme yok. Harf farkları merchant
  dağılımında ayrı satır olur (CAT adımları).
- Backend belirler: yön (Purchase → Debit, Salary → Credit), para birimi (hesabınki), `occurredAt` (sunucu saati),
  kategori alanları (yeni işlem "Sınıflandırılmamış").

Eşzamanlılık: bakiye `Balance + delta` olarak, "yeni bakiye 0 ile üst sınır arasında" şartıyla tek `UPDATE`'te
değişir. Bakiye 100 iken aynı anda gelen iki 60'lık harcamadan biri 201, diğeri 409 alır; bakiye 40 olur. Önceden
ayrı bir bakiye kontrolü yapılmaz (araya başka istek girebileceği için tek karar veren atomik güncellemedir).

Sorgular (geçici testle ölçüldü): 8 komut — müşteri, sahiplik, hesabın para birimi; aynı kimlik kontrolü, ekleme,
bakiye güncellemesi; geri okumada saat dilimi ve tek join'li okuma.

Bilinen sınırlar: aynı istek iki kez gönderilirse iki işlem oluşur (API-12); NUL gibi işlem kurallarına takılan
girdi API-13'ten beri istemcinin alanında (`description`) raporlanır; cevap yeni bakiyeyi taşımaz (hesap ayrıca okunur);
maaşı istemcinin yazabilmesi yalnız sentetik demo içindir.
