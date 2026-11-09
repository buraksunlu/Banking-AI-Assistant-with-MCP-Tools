# API sözleşmesi

Yerel adres `http://localhost:5248`; OpenAPI `/openapi/v1.json` (geliştirme). Giriş ve token kuralları
`docs/security.md`'dedir. Bu belge API-15'te örnek istek/cevaplarla genişletilecek.

## Endpoint özeti (16 Ekim 2026)

Makine okunur sözleşme: `docs/openapi/v1.json`; tarayıcıda `http://localhost:5248/scalar` (geliştirmede).

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
| `GET /api/analytics/spending-summary`, `category-breakdown`, `merchant-breakdown`, `merchant-spending` | evet | uygulandı | ANA-09 (1. parça) |
| `GET /api/analytics/period-comparison`, `monthly-trend`, `highest-transactions`, `recurring-payments` | evet | uygulandı | ANA-09 (2. parça) |
| `GET /api/analytics/upcoming-payments` | evet | uygulandı | ANA-12 |
| `GET /api/analytics/budget-check` | evet | uygulandı | ANA-13 |
| `GET /api/analytics/savings-simulation` | evet | uygulandı | ANA-14 |
| `GET /api/analytics/income-share` | evet | uygulandı | INC-01 |
| `GET /api/analytics/spending-advice` | evet | uygulandı | INC-02 |
| `GET /api/analytics/what-if/one-off` | evet | uygulandı | INV-01 |
| `GET /api/analytics/merchants`, `merchant-group-spending` | evet | uygulandı | ANA-10 |
| `GET /api/transactions` (bütün hesaplarda filtreli arama) | evet | uygulandı | ANA-09 (3. parça) |
| `POST /api/assistant/messages`, `GET /api/assistant/status` (sohbet asistanı) | evet | uygulandı | AI-14b + AI-07 |
| `GET /api/assistant/suggestions` (sohbet açılışında önerilen sorular) | evet | uygulandı | CHAT-06 |

Ortak davranış: müşteri yalnız token'dan gelir (istekte müşteri parametresi yoktur); başka müşterinin
kaynağı olmayan kaynakla birebir aynı `404`'ü alır; GUID olmayan kimlik `400`'dür.

## OpenAPI sözleşmesi ve örnekler (API-15, 16 Ekim 2026)

- **Sözleşme:** geliştirmede `GET /openapi/v1.json` (token gerekmez); commit'lenmiş kopyası `docs/openapi/v1.json`.
  `OpenApiContractTests` üretilen belgeyi bu dosyayla birebir karşılaştırır: sözleşmeyi değiştiren her değişiklik
  testi kırmızı yapar; bilerek değiştirildiyse dosya `BANKINGAI_UPDATE_OPENAPI=1 dotnet test tests/BankingAI.Api.Tests
  --filter "FullyQualifiedName~The_document_is_the_same"` ile yenilenir ve PR'da diff olarak görünür.
- **Belgenin doğruluğu** (`Api/OpenApi/ApiDocument` transformer'ları): enum'lar tam adlarıyla `string` (üretici
  `integer` yazıyordu), para `number` + `format: decimal` (üretici `number|string` + `double` yazıyordu), 21 korumalı
  işlemde `Bearer` güvenlik şeması, her hata cevabı `application/problem+json` + `Problem` şeması; ortak cevaplar kurala
  göre (500 her işleme, 401 korumalılara, 400 girdisi olanlara, 404 `id`/`accountId` parametresi olanlara), işleme
  özgü olanlar `[ProducesResponseType]` ile (201, 409, 429, 503). Yalnız `application/json` listelenir. Açıklamalar
  koddaki `/// <summary>`'den gelir (İngilizce; başlık ilk cümle, tam metin açıklama). Not: XML yorum üreticisi yalnız
  `AddOpenApi(options => …)` lambda biçimini yakalar; metot adıyla verilirse açıklamalar sessizce kaybolur.
- **Tarayıcı:** geliştirmede `http://localhost:5248/scalar` (Scalar, `Scalar.AspNetCore` 2.17.13): endpoint'ler,
  şemalar, örnek istekler; "Authentication" kutusuna login'den gelen token girilip denenebilir.
- **Çalıştırılabilir örnekler:** repo kökünde `api-examples.http` (VS Code "REST Client"): login → token → 26 GET örneği
  ve hata örnekleri; şifre `.env`'den (`$dotenv`) okunur. Yazma örnekleri demo verisini bozmamak için yorum satırıdır.

## Test kapsamı ve endpoint taraması (API-14, 16 Ekim 2026)

`tests/BankingAI.Api.Tests/EndpointSweepTests.cs` API'nin gerçek route tablosunu (`EndpointDataSource`) okur ve her korumalı
endpoint için tuttuğu geçerli örnek istekle karşılaştırır. **Yeni bir endpoint, tabloya örnek eklenmeden testleri geçemez.**
Tablo üç kuralı denetler: (1) token istemeyen endpoint'ler tam olarak `GET /api/health`, `POST /api/auth/login` ve
`GET /openapi/v1.json`'dır; (2) 21 korumalı endpoint'in hepsi token'sız 401 `unauthorized`; (3) hepsi var olmayan müşterinin
token'ına 401 `session_invalid` verir (örnek geçerli olduğu için 400 değil). Yazma örnekleri yalnız var olmayan müşteri için
gönderilir; demo verisine yazılmaz. Diğer API-14 testleri: boş ve `null` gövde üç yazma endpoint'inde 400 "İstek gövdesi
zorunlu." (ASP.NET'in iki bağımsız koruması: gövde bağlayıcısı ve örtük zorunlu parametre; ikisi birlikte kapanınca test
kırmızı); eşzamanlı hesap açma (anahtarsız iki farklı `ACC-n`, aynı anahtarla tek hesap); kalıcılık (yazılan hesap ve
işlemin satırı doğrudan veritabanından okunur: sahibi token'ın müşterisi, yön/para birimi/zaman/kategori backend'in; yeni
bir API örneği aynı veriyi okur).

## Genel JSON kuralları (API-03, 12 Ekim 2026, kullanıcı kararları)

- **Enum'lar yazıyla taşınır:** `"accountType": "Personal"`, `"kind": "Purchase"`. Girdi olarak yalnız
  **tam ad** kabul edilir; `2` gibi bir sayı ve `"purchase"` gibi farklı harf büyüklüğü 400'dür (API-13, kullanıcı
  kararı 4-A; `?period=lastmonth` da). Gerekçe: insanlar ve LLM için okunur; sessiz dönüşüm yok.
- **Para JSON sayısıdır:** `"balance": 577490.18`. `decimal` birebir yazılır (`2595.00` → `2595.00`). Okuma katıdır
  (API-15, karar K1-A): tırnaklı tutar (`"amount":"100"`) 400 `errors.amount` "Geçersiz değer."; web varsayılanı bunu
  sessizce sayı sayardı.
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
| Aynı `Idempotency-Key` farklı bir istekle (API-12) | 409 | `idempotency_conflict` | Bu anahtar farklı bir istek için kullanılmış |
| JSON olmayan gövde | 415 | `unsupported_media_type` | Desteklenmeyen içerik türü; JSON gönderin. |
| Login deneme sınırı (IP başına dakikada 5); sohbet mesajı sınırı (müşteri başına dakikada 10); `Retry-After` başlığıyla | 429 | `too_many_requests` | Çok fazla deneme; biraz sonra tekrar deneyin. |
| Asistan yanlış/eksik cevap üretti ya da AI servisiyle sözleşme uyuşmadı (AI-14b) | 502 | `assistant_failed` | Kılavuz bu soruya cevap üretemedi; farklı sormayı deneyin. |
| AI servisi kapalı, modeli yok ya da banka servisine ulaşamıyor (AI-14b) | 503 | `assistant_unavailable` | Kılavuz şu an kullanılamıyor. |
| Asistan zamanında cevap vermedi (AI-14b) | 504 | `assistant_timeout` | Kılavuz zamanında cevap veremedi; tekrar deneyin. |
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

İsteğe bağlı `Idempotency-Key` başlığıyla tekrar gönderilen istek ikinci hesap açmaz (API-12, aşağıda).
Bilinen sınır: müşteri başına hesap sayısı sınırı yok (kullanıcı kararı; ACC-LIMIT); anahtarsız istek iki kez
gönderilirse iki hesap açılır.

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
| `period` | isteğe bağlı, yalnız adla: `ThisMonth`, `LastMonth`, `Last3Months`, `Last6Months`, `Today`, `Yesterday`, `ThisWeek`, `LastWeek`, `Last7Days`, `Last30Days`, `ThisYear` |
| `from`, `to` | isteğe bağlı, `yyyy-MM-dd`, **birlikte** (yerel tarih, dahil; özel dönem en fazla 24 ay); `period` ile birlikte verilemez |
| (ikisi de yoksa) | **son 3 ay** (kullanıcı kararı) |
| `period=AllTime` | bütün geçmiş (API-17): iş gününün sonuna kadar; cevapta dönemin başı en eski işlemin yerel günü (yoksa bugün) |
| `limit` | 1–50, varsayılan 20 |
| `cursor` | isteğe bağlı; önceki sayfanın `nextCursor`'ı (opak metin, olduğu gibi geri gönderilir; bozuksa `400` `errors.cursor`) |

Tür/yön/kategori/merchant filtreleri bu endpoint'te yoktur (kullanıcı kararı); filtreli arama `GET /api/transactions`'tadır,
tek hesap için `accountId` ile (ANA-09 3. parça, karar K2-A).

```json
{
  "period": { "kind": "Custom", "startUtc": "2026-09-30T21:00:00+00:00", "endUtc": "2026-10-10T21:00:00+00:00",
              "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "totalCount": 8,
  "hasMore": true,
  "nextCursor": "AQjf…",
  "items": [ { "id": "30000000-0000-0000-0000-000000000604", "kind": "Purchase", "amount": 145.00, "...": "..." } ]
}
```

| Durum | Cevap |
|---|---|
| Kendi hesabı | `200` + sayfa (işlem yoksa `items: []`, `totalCount: 0`) |
| Başka müşterinin / olmayan hesap | `404` (birebir aynı gövde) |
| Bozuk parametre (limit sınırı, bozuk imleç, tek tarih, `AllTime` + tarih, `from` > `to`, 24 aydan uzun, `period` + tarih, `period=Custom`/`2`/`Foo`, geçersiz tarih) veya GUID olmayan kimlik | `400` |
| Token yok / geçersiz | `401` |

Sayfalama imleçlidir (API-17, offset kalktı): sonraki sayfa `cursor=<nextCursor>` ile istenir, son sayfada `nextCursor: null`.
İmleç son işlemin anı ve kimliğidir; gezilirken eklenen yeni işlem listenin başına gelir (aşağı çekince görünür), sayfa kenarında
tekrar ya da atlama olmaz. `totalCount` her sayfada bütün sonucun sayısıdır. Eski `offset` parametresi artık tanınmaz ve yok
sayılır (ASP.NET bilinmeyen sorgu parametresini yok sayar). Bilinen sınırlar: dönem "bugün"ü iş saatine göredir (`BANKINGAI_DEMO_NOW`, ANA-09);
silinmiş müşterinin token'ı `401` `session_invalid` (API-13).

## `GET /api/transactions` — filtreli arama (ANA-09 3. parça, 16 Ekim 2026)

Token gerekir. Müşterinin **bütün hesaplarında** (Business dükkân dahil) ekstre gibi arama: bütün türler, en yeniden
eskiye (eşitse kimliğe göre), karşı taraf maskeli. Hesap ve arama `TransactionSearchService.SearchAsync`'tedir (ANA-05);
controller yalnız parametreleri okur. Analytics'ten farkı: harcama analizi değil işlem listesidir, bu yüzden işletme
hesabı `400` almaz.

| Parametre | Kural |
|---|---|
| `period` / `from` + `to` | `GET /api/accounts/{accountId}/transactions` ile aynı (`AllTime` dahil); **ikisi de yoksa son 3 ay** (karar K1-B: liste, rakam değil; cevap dönemini söyler) |
| `accountId` | isteğe bağlı; yalnız kendi hesabı (başkasınınki `404`), Business da olur |
| `kind` | isteğe bağlı, tam adla: `Purchase`, `Sale`, `Transfer`, `Salary`, `OpeningFunding` (`purchase`, `1` → `400`) |
| `direction` | isteğe bağlı, tam adla: `Debit`, `Credit` |
| `categoryCode` | isteğe bağlı, tanımlı kod (ör. `FOOD`; bilinmeyen kod `400` `errors.categoryCode`: "yok" ile "bu kategoride işlem yok" ayrılır) |
| `onlyUnclassified` | `true` → yalnız kategorisiz harcamalar; `categoryCode` ile birlikte `400` |
| `merchant` | isteğe bağlı; müşterinin kendi merchant'larıyla eşleştirilir (`MerchantMatcher`), tahmin yok |
| `limit` / `cursor` | 1–50 (varsayılan 20) / önceki sayfanın `nextCursor`'ı (API-17) |
| `currencyCode` | isteğe bağlı, birebir `TRY`/`USD`/`EUR`/`GBP` (`try`, `JPY` → `400`) |
| `minAmount` / `maxAmount` | isteğe bağlı, **dahil** sınırlar (ANA-11); verilirse `currencyCode` zorunlu (eşik para birimleri arasında karşılaştırılmaz); `min ≤ max`; 0,01–üst sınır, en fazla 2 ondalık |
| `accountType` | isteğe bağlı, tam olarak `Personal` ya da `Business` (`personal`, `1` → 400); hesaplar bu türe daralır, `accountId` ile **kesişir** (uyumsuz çift hata değil, boş liste), merchant adayları da aynı kapsamdan gelir (CHAT-07: harcama rakamları yalnız şahsi hesapları sayar, sohbet bağlantısı aynısını ister) |

**Tutar yazımı:** yalnız rakam ve nokta (`500`, `500.50`). Virgül, binlik ayırıcı, işaret ve boşluk `400` alır
(`'500,50' geçerli bir değer değil.`). Neden: ASP.NET'in kendi decimal bağlaması binlik ayırıcıya izin verir ve `500,50`'yi
sessizce **50050** okurdu; tutarlar bu yüzden `Api/Queries/AmountParameter` ile katı okunur (ANA-11'de testle yakalandı).

Kategori filtreleri yalnız harcamaya uygulanır: `kind=Transfer&categoryCode=FOOD` boş sonuç değil `400` `errors.kind`.
Enum parametreleri `Api/Queries/ExactEnum` ile tam adla okunur (dönem ve karşılaştırma adları da aynı yardımcıyı kullanır).

```json
{ "period": { "kind": "Last3Months", "startDate": "2026-07-10", "endDate": "2026-10-10", "…": "…" },
  "merchantMatch": { "status": "Matched", "merchant": "Starbucks", "isPartialMatch": true, "candidates": [] },
  "totalCount": 5, "hasMore": true, "nextCursor": "AQjf…",
  "items": [ { "localDate": "2026-10-08", "kind": "Purchase", "amount": 165.00, "merchant": "Starbucks", "…": "…" } ] }
```

`merchantMatch` yalnız `merchant` verilince doludur; `Ambiguous`/`NotFound` durumunda `totalCount: 0`, `items: []` ve
`candidates` nedenini söyler. Ada bu ay: 8 işlem (maaş 45.000, transferler 1.500 "Deniz M***" ve 300 "Ege Y***",
5 harcama); `kind=Transfer&direction=Debit` → 1.500; `onlyUnclassified=true` → 145; `categoryCode=FOOD` → 165, 95, 210,
120. Deniz bu ay: 7 işlem (dükkân satışları dahil). `period=AllTime` (API-17): Ada 49 işlem, dönem `2025-04-01 – 2026-10-10`
(demo anıyla), 20'lik sayfalarla 20 / 20 / 9; Selin 646 işlem, 50'likle 13 sayfa, tekrarsız. Analytics endpoint'lerinde
`AllTime` dönem değildir (`400` `errors.period`; mesaj adı listelemez). Bilinen sınır: serbest metin araması yok (ANA-05 kararı).

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

`Idempotency-Key` başlığı **zorunludur** (API-12, aşağıda); anahtarsız istek 400 `errors.idempotencyKey`.

Bilinen sınırlar: NUL gibi işlem kurallarına takılan
girdi API-13'ten beri istemcinin alanında (`description`) raporlanır; cevap yeni bakiyeyi taşımaz (hesap ayrıca okunur);
maaşı istemcinin yazabilmesi yalnız sentetik demo içindir.

## Tekrar gönderilen istek: `Idempotency-Key` (API-12, 13 Ekim 2026)

Bağlantı koptuğunda istemci isteği tekrar gönderebilir; para iki kez hareket etmemelidir. İstemci her **yeni** yazma
için rastgele bir anahtar üretir (ör. UUID) ve tekrarlarda aynısını gönderir:

```http
POST /api/accounts/{accountId}/transactions
Idempotency-Key: 7c2f9a1e-…

1. istek                         → 201 Created, işlem yazılır, bakiye değişir
aynı anahtar + aynı gövde        → 201 Created + "Idempotent-Replayed: true", aynı gövde ve Location, bakiye DEĞİŞMEZ
aynı anahtar + farklı gövde/hesap → 409 idempotency_conflict, hiçbir şey yazılmaz
```

| Kural | Değer |
|---|---|
| Nerede | İşlem ekleme: **zorunlu**; hesap açma: isteğe bağlı (yoksa her istek yeni hesap) |
| Biçim | 1–100 görünür ASCII karakter, boşluk yok (geçersiz → 400 `errors.idempotencyKey`) |
| Aynı istek sayılan | İstemcinin söylediği her şey: hesap, tür, tutar, açıklama, merchant (hesapta ad ve para birimi). Sunucunun atadığı zaman ve kategori karşılaştırılmaz |
| Başarısız ilk deneme | Saklanmaz: ör. 409 yetersiz bakiyeden sonra aynı anahtar, bakiye yettiğinde başarılı olur |
| Süre | Dolmaz: anahtar, oluşturduğu kaydın kimliği gibidir; yeni işlem = yeni anahtar |

Nasıl çalışır: kaydın kimliği (GUID) müşteri + kaynak türü + anahtardan hesaplanır (`IdempotentId`, SHA-256, RFC 9562
sürüm 8). Aynı üçlü hep aynı kimliği verir; veritabanı aynı kimliği ikinci kez kabul etmez. Yazıcı aynı kimliği
bulursa içeriği karşılaştırır: aynıysa hiçbir şey yazmadan "zaten vardı" der (`WriteOutcome.AlreadyExisted`), farklıysa
`IdempotencyConflictException` (409). Aynı anda gelen iki tekrardan ikincisi birincil anahtara takılır; yazıcı geri alır
ve kayıtlı satırı aynı şekilde karşılaştırır. Farklı müşteriler aynı anahtarı kullansa da kimlikler çakışmaz. Migration
yoktur. İki bacaklı yazıcı aynı kuralı `PairId` için uygular (KIND-03 kararı: aynı çift → ilk sonuç, farklı → 409).

Kullanıcı kararları (API-12): anahtardan türetilen kimlik (1-A), işlemde zorunlu / hesapta isteğe bağlı (2-A), tekrar
= aynı 201 + `Idempotent-Replayed: true` (3-A), farklı içerik 409 (4-A), başarısızlık saklanmaz (5-A), kapsam iki yazıcı +
iki endpoint (6-A).

## İş saati ve demo "şimdi" (ANA-09, karar 2-A)

"Bu ay", "son 3 ay" gibi dönemlerin ve yeni eklenen işlemin zamanının dayandığı saat **iş saati**dir
(`Api/Time/BusinessClock`). İsteğe bağlı `BANKINGAI_DEMO_NOW` ayarı (tam olarak `2026-10-10T12:00:00Z` biçiminde, UTC)
verilirse iş saati o ana sabitlenir; demo verisi her gün aynı cevapları verir. Ayar yoksa gerçek saat kullanılır.
Giriş ve token süresi **her zaman gerçek saattir** (iş saati token'a uygulanmaz; aksi hâlde token verilir verilmez
süresi dolmuş sayılırdı). Biçimi bozuk ya da gelecekteki bir değer API'yi açılışta durdurur (gelecek tarihli işlem
yazıcıda reddedilir). Sınır: ayar açıkken eklenen bütün işlemler aynı ana düşer (sıra kimliğe göre).

## Dönem parametreleri (bütün endpoint'lerde aynı, `Api/Queries/PeriodParameters`)

Ya `period` (tam adla: `ThisMonth`, `LastMonth`, `Last3Months`, `Last6Months`, `Today`, `Yesterday`, `ThisWeek`, `LastWeek`,
`Last7Days`, `Last30Days`, `ThisYear`; anlamları docs/analytics.md, ANA-20) ya da `from` + `to` (yerel tarih, dahil;
en fazla 24 ay). İkisi birlikte, tek tarih, bilinmeyen ad, `Custom`, sayı (`2`) ya da farklı harf büyüklüğü → 400.
Hesabın işlem listesinde dönem verilmezse son 3 ay; **analytics'te dönem zorunludur** (karar 3-A; yoksa 400
`errors.period`). Düzeltme (ANA-09): API-09'dan beri `Last3Months` ve `Last6Months` adları "içinde rakam var"
kontrolüne takılıp 400 alıyordu; artık yalnız tanımlı adlarla birebir eşleşme aranıyor.

## Analytics (ANA-09)

Token gerekir; müşteri token'dan gelir. Bütün hesaplar servislerdedir (docs/analytics.md); cevaplar servis kayıtlarıdır
(karar 4-A; müşteri/hesap kimliği taşımazlar). Para birimleri ayrı satırdır; harcama yoksa liste boştur (uydurma "0"
yok). `accountId` isteğe bağlıdır: başkasının hesabı `404`, harcama analizinde işletme hesabı `400`
(`errors.accountId`). Beklenen değerler demo anı (10 Ekim 2026) içindir.

### `GET /api/analytics/spending-summary?period=…[&accountId=…]`

Harcama (`Kind = Purchase`, şahsi hesaplar) para birimi başına; kategorisiz kısım ayrıca.

```json
{ "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10", "…": "…" },
  "currencies": [ { "currencyCode": "TRY", "totalAmount": 735.00, "transactionCount": 5,
                    "uncategorizedAmount": 145.00, "uncategorizedCount": 1 } ] }
```

Ada bu ay 735 (1.500 TL transfer harcama değildir); Deniz geçen ay 31.000 (1 işlem); Ada Ocak 2026 → `currencies: []`.

### `GET /api/analytics/category-breakdown?period=…`

Para birimi başına kategoriler, büyükten küçüğe; yüzde backend'de (2 ondalık). Kategorisiz grup `categoryCode: null`,
`displayName: "Sınıflandırılmamış"`; `OTHER` ile birleşmez. Ada bu ay: FOOD 590 (%80,27), Sınıflandırılmamış 145 (%19,73).

### `GET /api/analytics/merchant-breakdown?period=…`

Merchant başına harcama; merchant'sız grup `merchant: null`. Ada bu ay: Starbucks 375 (%51,02), Kahve Durağı 215
(%29,25), Sınıflandırılmamış 145 (%19,73).

### `GET /api/analytics/merchants[?accountId=…]` ve `GET /api/analytics/merchant-group-spending?merchants=…&merchants=…&period=…` (ANA-10)

Kavramsal sorular için kapalı küme ("kahveye ne kadar harcadım?"). `merchants`: müşterinin şahsi hesaplarında bugüne kadar
alışveriş yaptığı merchant'lar (kanonik ad, son kategori, son alışveriş günü, işlem sayısı). `merchant-group-spending`:
bu listeden seçilen 1–20 ad (tekrarlı `merchants` parametresi), dönem zorunlu. Ad büyük/küçük harf ve Türkçe işaret
farkıyla kabul edilir, cevapta kanonik adla döner; **kısmi ad kabul edilmez**, liste dışı ad 400 `errors.merchants`.

```json
{ "period": { "kind": "ThisMonth", "…": "…" },
  "merchants": ["Starbucks", "Kahve Durağı"],
  "currencies": [ { "currencyCode": "TRY", "totalAmount": 590.00,
                    "merchants": [ { "merchant": "Starbucks", "displayName": "Starbucks", "amount": 375.00, "transactionCount": 2, "percentage": 63.56 },
                                   { "merchant": "Kahve Durağı", "displayName": "Kahve Durağı", "amount": 215.00, "transactionCount": 2, "percentage": 36.44 } ] } ] }
```

Dönemde harcaması olmayan seçilmiş merchant `merchants` listesinde görünür, dökümde satırı olmaz. Hatalar (400): ad yok
("1 ile 20 arasında merchant verilmeli."), `Starbuck` ("'Starbuck' müşterinin merchant listesinde yok."), başka müşterinin
merchant'ı, dönem yok. Token yoksa 401, başkasının hesabı 404, işletme hesabı 400.

### `GET /api/analytics/merchant-spending?merchant=…&period=…`

Metin müşterinin **kendi** merchant'larıyla eşleştirilir (`MerchantMatcher`); tahmin yapılmaz.

| `match.status` | Anlamı | Örnek |
|---|---|---|
| `Matched` | Tek merchant bulundu (`isPartialMatch` true ise kısmi eşleşme) | Ada, `starbucks` / `starbuck`, son 3 ay → 930 TRY, 5 işlem |
| `Ambiguous` | Birden çok aday; tutar yok, `candidates` dolu | Ege, `şehir` → Şehir Ulaşım, Şehirlerarası Otobüs |
| `NotFound` | Müşterinin böyle bir merchant'ı yok | Ada, `Aile Marketi` (Deniz'in) |

`merchant` yoksa ya da boşsa `400` (`errors.merchant`).

### `GET /api/analytics/period-comparison?comparison=…` ya da `?currentFrom=…&currentTo=…&previousFrom=…&previousTo=…`

İki dönemin harcaması para birimi başına: toplamlar, adetler, fark (`change` = şimdiki − önceki) ve yüzde; kategoriler
farkın mutlak değerine göre büyükten küçüğe (artış da azalış da "en büyük neden" önce). Karşılaştırma **zorunludur**
(karar K1-A): `comparison` tam adla (`PreviousFullMonth` = son tam ay vs önceki ay, "geçen aya göre"; `MonthToDate` =
bu ayın bugüne kadarı vs önceki ayın aynı günleri) **ya da** dört tarihin dördü birden (kendiliğinden `Custom`;
`comparison=Custom` adı kabul edilmez). Ad + tarih, eksik tarih, bilinmeyen ad/sayı/farklı harf büyüklüğü → 400;
çakışan dönemler → 400 `errors.previousFrom`. Önceki dönem 0 ise `percentageChange: null` (sıfırdan büyüme oranı yok).

```json
{ "current":  { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30", "…": "…" },
  "previous": { "kind": "Custom",    "startDate": "2026-08-01", "endDate": "2026-08-31", "…": "…" },
  "currencies": [ { "currencyCode": "TRY", "currentAmount": 31000.00, "currentCount": 1, "previousAmount": 24000.00,
                    "previousCount": 1, "change": 7000.00, "percentageChange": 29.17,
                    "categories": [ { "categoryCode": "GROCERY", "displayName": "…", "currentAmount": 31000.00,
                                      "previousAmount": 24000.00, "change": 7000.00 } ] } ] }
```

Deniz `PreviousFullMonth` yukarıdaki; Ege 1.530 / 2.100 (−570, %−27,14; TRAVEL −1.200 önce); Deniz `MonthToDate`
11.350 / 0 (`percentageChange: null`); Ada Eylül / Temmuz 624,98 / 514,98.

### `GET /api/analytics/monthly-trend?months=…[&categoryCode=… | &merchant=…]`

Son `months` tam takvim ayı + devam eden ay (`isPartial: true`), eskiden yeniye. `months` **zorunlu**, 1–24 (karar K2-A).
Harcama olmayan ay `amount: 0` ile görünür, atlanmaz. `categoryCode` (tanımlı kod, ör. `FOOD`) ya da `merchant`
(`MerchantMatcher`, tahmin yok; `merchantMatch` alanı eşleşmeyi anlatır, eşleşme yoksa `currencies: []`) verilebilir,
ikisi birlikte 400 `errors.merchant`. Tanımsız kategori kodu 400 `errors.categoryCode` (ANA-09'da düzeltildi; önceden
`errors.query` görünürdü). Cevap: `months` (her ayın penceresi, `period` ile), `merchantMatch` (filtre yoksa `null`),
`currencies[].months[]` (`year`, `month`, `isPartial`, `amount`, `transactionCount`).
Deniz `months=6`: 12.000, 14.000, 16.000, 18.000, 24.000, 31.000, 11.350 (Ekim kısmi); Ege TRAVEL: yalnız Ağustos 1.200.

### `GET /api/analytics/highest-transactions?period=…[&limit=…][&includeAllTransactions=true][&currencyCode=…&minAmount=…&maxAmount=…]`

Para birimi başına en büyük işlemler (`TransactionItem`), tutar büyükten küçüğe, eşitse yeni olan önce. Dönem zorunlu;
`limit` 1–100, varsayılan 5. Varsayılan kapsam harcamadır (şahsi hesap + `Purchase`); `includeAllTransactions=true`
maaş, transfer ve işletme hesabının satışlarını da katar (ekstre kapsamı; işletme hesabı `accountId` ile seçilebilir).
Ada bu ay: 210, 165, 145, 120, 95; tüm işlemlerle: maaş 45.000, transfer 1.500 ("Deniz M***"), transfer 300, 210, 165.
Tutar aralığı (ANA-11) aramadakiyle aynı kurallar ve aynı katı yazımla: dahil sınırlar, `currencyCode` zorunlu, `limit` filtreden
sonra. Ada, TRY, `maxAmount=150` → 145, 120, 95; Deniz, son 6 ay, `limit=3`, TRY, `maxAmount=20000` → 18.000, 16.000, 14.000.

### `GET /api/analytics/recurring-payments`

Son 6 ayda aylık tekrar eden ödemeler (≥3 ödeme, 26–35 gün aralık, ±%10 tutar, son ödeme ≤35 gün önce) ve para birimi
başına aylık toplam (son tutarların toplamı). **Dönem parametresi yoktur** (karar K3-A): 6 ay kuralın kendisidir ve
cevaptaki `period` alanında görünür; gönderilen `period` yok sayılır. `pattern` neden düzenli sayıldığını anlatır;
`nextExpectedDate` son ödeme + 1 aydır, **tahmindir, söz değildir**. Ada: Netflix 229,99 (6 ödeme, sıradaki 12 Ekim),
Spotify 99,99 (18 Ekim), toplam 329,98 TRY. Deniz ve Ege: boş liste (market tutarı büyüyor; ulaşım ayda iki kez).

Dört endpoint'te de: token yoksa 401, başkasının hesabı 404, harcama analizinde işletme hesabı 400 (`errors.accountId`;
yalnız `includeAllTransactions=true` ile en yüksek işlemlerde kabul edilir).

### `GET /api/analytics/upcoming-payments[?days=…][&accountId=…]` (ANA-12)

Düzenli ödemelerin önümüzdeki `days` gün içindeki **tahmini** tarihleri; `days` 1–31, verilmezse 30 (karar K3-A). Pencere
`from` (bugün) – `to` (bugün + gün, dahil). Tahmini tarihi geçmiş ama henüz ödenmemiş kalem `isOverdue: true` ve negatif
`daysUntil` ile listelenir (K1-A). `estimatedDate` = son ödeme + 1 ay (her zamanki günü), `estimatedAmount` = son ödemenin
tutarı; ikisi de **tahmindir**, `pattern` kanıtıdır. `totals` para birimi başına `estimatedTotal`.

```json
{ "from": "2026-10-10", "to": "2026-11-09",
  "payments": [ { "merchant": "Netflix", "categoryCode": "SUBSCRIPTION", "categoryDisplayName": "Abonelik",
                  "accountName": "Ada günlük TRY", "currencyCode": "TRY", "estimatedDate": "2026-10-12",
                  "estimatedAmount": 229.99, "daysUntil": 2, "isOverdue": false, "pattern": { "occurrenceCount": 6, "…": "…" } },
                { "merchant": "Spotify", "estimatedDate": "2026-10-18", "estimatedAmount": 99.99, "daysUntil": 8, "…": "…" } ],
  "totals": [ { "currencyCode": "TRY", "estimatedTotal": 329.98 } ] }
```

`days=5` → yalnız Netflix (`to` 2026-10-15). `days` 0, 32 veya `abc` → 400 `errors.days`. Token yoksa 401, başkasının
hesabı 404, işletme hesabı 400 (`errors.accountId`). Deniz ve Ege: boş listeler.

### `GET /api/analytics/budget-check?amount=…&currencyCode=…[&categoryCode=… | &merchant=…][&accountId=…]` (ANA-13)

Kullanıcının söylediği bütçeye göre **bu ayın** durumu; bütçe saklanmaz. `amount` zorunlu (tutar yazımı aramadaki gibi katı:
yalnız rakam ve nokta), `currencyCode` zorunlu. `categoryCode` ve `merchant` verilmezse bütçe toplam harcama içindir;
ikisi birlikte 400 `errors.merchant`. Merchant eşleşmezse `figures: null` ve `merchantMatch` nedenini söyler.

```json
{ "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10", "…": "…" },
  "currencyCode": "TRY", "budgetAmount": 1000, "categoryCode": "FOOD", "categoryDisplayName": "Yeme & İçme",
  "merchantMatch": null,
  "figures": { "spentAmount": 590.00, "transactionCount": 4, "remainingAmount": 410.00, "overAmount": 0,
               "isOverBudget": false, "elapsedDays": 10, "daysInMonth": 31, "dailyAverage": 59.00,
               "projectedMonthEnd": 1829.00, "projectedOverAmount": 829.00, "willExceed": true,
               "projectionMethod": "LinearDailyAverage" } }
```

Hatalar (400): `amount` yok ("Bütçe tutarı zorunlu."), `1000,50`, `0`; `currencyCode` yok ("Bütçenin para birimi
(currencyCode) verilmeli.") veya `try`; kategori + merchant; tanımsız kategori (`errors.categoryCode`). Token yoksa 401,
başkasının hesabı 404, işletme hesabı 400 (`errors.accountId`).

### `GET /api/analytics/savings-simulation?reductionPercent=…[&categoryCode=… | &merchant=…][&months=…][&accountId=…]` (ANA-14)

"Şunu %N azaltsam ne kazanırım?": son `months` tam ayın (varsayılan 6, 1–24; harcamasız aylar dahil) aylık ortalaması ×
oran, aylık ve yıllık. `reductionPercent` zorunlu, tam sayı 1–100. Kategori ve merchant verilmezse toplam harcama. Para
birimi başına satır; harcama yoksa ya da merchant eşleşmezse `currencies: []`. Yatırım getirisi değildir; varsayım
`method` alanında yazılıdır.

```json
{ "from": "2026-04-01", "to": "2026-09-30", "months": 6, "reductionPercent": 50, "categoryCode": null,
  "merchantMatch": { "status": "Matched", "merchant": "Starbucks", "…": "…" },
  "method": "FullMonthAverageTimesReduction",
  "currencies": [ { "currencyCode": "TRY", "totalSpent": 1110.00, "monthlyAverage": 185.00, "monthlySaving": 92.50,
                    "annualSaving": 1110.00, "monthlyAmounts": [ { "year": 2026, "month": 4, "isPartial": false, "amount": 185.00, "transactionCount": 1 }, "…" ] } ] }
```

Hatalar (400): oran yok ("Azaltma oranı zorunlu."), `0`/`101` ("1 ile 100 arasında olmalı."), `abc`; `months=25`; kategori +
merchant; tanımsız kategori. Token yoksa 401, başkasının hesabı 404, işletme hesabı 400 (`errors.accountId`).

### `GET /api/analytics/income-share?period=LastMonth | ?from=YYYY-MM-01&to=YYYY-MM-son` (INC-01)

"Gelirimin ne kadarını harcadım?": tek bir **tamamlanmış** takvim ayının harcaması ve kategorileri, o ayla biten 3 tam ayın
aylık ortalama maaşının (`Kind == Salary`, kişisel hesaplar; maaşsız ay 0 sayılır) yüzdesi olarak. Pay, yuvarlanmış tabandan
değil tam gelir toplamından hesaplanır (2 ondalık, 100'e zorlanmaz, 100'ü geçebilir). Para birimi başına satır; o para
biriminde maaş yoksa `incomeBase`, `spentShare` ve kategori `incomeShare` alanları `null`. Kategori tutarları
`category-breakdown` ile aynıdır.

```json
{ "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30", "…": "…" },
  "incomeFrom": "2026-07-01", "incomeTo": "2026-09-30", "incomeMonths": 3, "method": "ThreeFullMonthSalaryAverage",
  "currencies": [ { "currencyCode": "TRY", "totalSpent": 7228.49, "incomeTotal": 24000.00, "incomeCount": 3,
                    "incomeBase": 8000.00, "spentShare": 90.36,
                    "categories": [ { "categoryCode": "FOOD", "displayName": "Yeme & İçme", "amount": 3746.50,
                                      "transactionCount": 35, "incomeShare": 46.83 }, "…" ] } ] }
```

Hatalar (400): dönem yok; `ThisMonth`, `Last3Months` vb. ("Gelir payı yalnız tam bir ay için hesaplanır…", `errors.period`);
ayın ilk–son günü olmayan ya da iki aylık aralık (`errors.from`); devam eden ay (`errors.to`); dönem adı + tarih birlikte.
Token yoksa 401. Hesap filtresi yok (gelir ve harcama müşterinin bütün kişisel hesaplarından).

### `GET /api/analytics/spending-advice?period=LastMonth | ?from=YYYY-MM-01&to=YYYY-MM-son` (INC-02)

"Nerede tasarruf edebilirim?": aynı ay kuralları ve aynı hatalarla (`income-share`'in 400 metinleri birebir) bir ayın harcaması
Temel / İsteğe bağlı / Bilinmiyor gruplarına ayrılır, kurallar tutarsa para birimi başına en fazla 2 öneri döner (kurallar ve
eşikler `docs/analytics.md` "INC-02"; eşikler cevapta `thresholds`). Öneri yoksa `advice` boş ve `reason` `NoRuleMatched` ya da
`NoIncome`; öneri varsa `reason` `null`. `message` .NET'in sabit Türkçe şablonudur (model yazmaz). `ratio` toplam kuralında ve
olağan harcaması olmayan kategoride `null`.

```json
{ "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30", "…": "…" },
  "incomeFrom": "2026-07-01", "incomeTo": "2026-09-30", "usualFrom": "2026-03-01", "usualTo": "2026-08-31",
  "savingsFrom": "2026-04-01", "savingsTo": "2026-09-30",
  "thresholds": { "discretionaryLimitPercent": 30, "usualRatio": 1.5, "minimumImpactPercent": 2, "usualMonths": 6,
                  "reductionPercent": 20, "maximumAdvice": 2 },
  "method": "RuleBasedAdvice",
  "currencies": [ { "currencyCode": "TRY", "totalSpent": 7228.49, "incomeTotal": 24000.00, "incomeBase": 8000.00, "spentShare": 90.36,
      "groups": [ { "group": "Essential", "amount": 4435.00, "incomeShare": 55.44 },
                  { "group": "Discretionary", "amount": 2313.49, "incomeShare": 28.92 },
                  { "group": "Unknown", "amount": 480.00, "incomeShare": 6.00 } ],
      "categories": [ { "categoryCode": "ENTERTAINMENT", "displayName": "Eğlence", "group": "Discretionary", "amount": 2253.50,
                        "incomeShare": 28.17, "usualMonthlyAmount": 927.83 }, "…" ],
      "advice": [ { "rule": "CategoryAboveUsual", "categoryCode": "ENTERTAINMENT", "categoryDisplayName": "Eğlence",
                    "amount": 2253.50, "reference": 927.83, "ratio": 2.43, "excess": 1325.67, "excessShare": 16.57,
                    "reductionPercent": 20, "monthlySaving": 236.18, "annualSaving": 2834.20,
                    "message": "Eylül 2026 ayında Eğlence harcamanız 2.253,50 TL oldu; …" } ],
      "reason": null } ] }
```

### `GET /api/analytics/what-if/one-off?period=…[&categoryCode=…]` (INV-01 PR1)

"Harcamasaydım ne olurdu?": dönemin (isteğe bağlı tek kategorinin) TRY harcaması dönemin ilk günü USD, EUR ve gram altın olarak
alınmış sayılır, müşterinin bugününde geçmiş fiyatla değerlenir. Dönem parametreleri harcama analizleriyle aynı (`AllTime` yok).
TRY harcama yoksa `spentAmount` 0 ve `assets` boş. Fiyat 10 günden eskiyse `status` `Stale`, hiç yoksa `Missing`; o satırda
`units`/`value`/`difference`/`differencePercent` `null`. `disclaimer` her cevapta aynı sabit metindir (yatırım tavsiyesi değildir).

```json
{ "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30", "…": "…" },
  "categoryCode": "ENTERTAINMENT", "categoryDisplayName": "Eğlence", "currencyCode": "TRY", "spentAmount": 2253.50,
  "buyDate": "2026-09-01", "valueDate": "2026-10-10", "method": "SingleBuyAtPeriodStart",
  "disclaimer": "Geçmiş fiyatlarla yapılmış bir hesaptır; …",
  "assets": [ { "symbol": "USDTRY", "name": "ABD Doları / Türk Lirası", "baseUnit": "USD",
                "buyDate": "2026-09-01", "buyPriceDate": "2026-09-01", "buyPrice": 48.1891,
                "valueDate": "2026-10-10", "valuePriceDate": "2026-10-09", "valuePrice": 49.1274,
                "units": 46.763687, "value": 2297.38, "difference": 43.88, "differencePercent": 1.95,
                "status": "Ok", "source": "TCMB" }, "…" ] }
```

Hatalar (400): dönem yok, dönem adı + tarih birlikte, tanımsız `categoryCode`; token yoksa 401.

## Sohbet asistanı (AI-14b + AI-07, 19 Ekim 2026)

iPhone yalnız .NET API ile konuşur; API token'ı, müşteriyi, hız sınırını ve mesajı kontrol edip mesajı aynı token'la
AI servisine (`POST /v1/chat`, docs/local-ai.md) iletir. AI servisi yalnız `127.0.0.1`'de dinler.

```text
iPhone ──Bearer──► POST /api/assistant/messages ── token → müşteri var mı (K10-A) → hız sınırı → mesaj kuralı
                         └── aynı Authorization başlığı ──► AI servisi /v1/chat ──► MCP server (araçlar, MCP-b)
```

### `POST /api/assistant/messages`

- İstek `{ "message": "Dün ne harcadım?" }` (dış sözleşme değişmedi; iPhone tarih göndermez). API, AI servisine mesajla birlikte
  müşterinin **kendi bugününü ve saat dilimini** iletir (`today` = `YYYY-MM-DD`, `timeZone`; CHAT-08 K9-C): `ICustomerCheck`
  (`CustomerDay`) iş saatinden (`BANKINGAI_DEMO_NOW` dahil) müşterinin saat diliminde hesaplar, AI servisi kendi saatini kullanmaz.
  Mesaj 1–500 karakter (`InputRules.Text`, DB gibi sayar, NUL reddedilir), boşluk
  olamaz, tanınmayan alan (ör. `customerId`) 400. Geçersiz mesaj AI servisine hiç gitmez.
- Cevap `AssistantReply`: `type` (`Answer` / `Clarification` / `OutOfScope`, tam adla), `answer` (Türkçe, Markdown
  olabilir), `toolCalls` (çalışan sorgu + kontrol edilmiş argümanlar; AI-11/CHAT-07 için) ve `facts` (cevabın dayandığı,
  backend'in hesaplayıp biçimlendirdiği kalemler) ve `suggestions` (CHAT-06: cevabın altında önerilecek 0–3 soru, sabit tablodan;
  aynen geri gönderilen öneri model araç seçmeden çalışır, docs/local-ai.md "Önerilen sorular"). Modelin adı dışarı verilmez, logda kalır (K2-A).
  `links` (CHAT-07, her zaman var, çoğu zaman boş): cevabın altındaki "İşlemleri gör" düğmeleri. Bağlantıyı **model değil .NET**
  kurar (`Api/Assistant/AssistantLinks`, kontrol edilmiş `toolCalls`'tan): `{ title, query: { period, from, to, kind, accountType,
  categoryCode, merchant } }`, `GET /api/transactions`'ın aynı rakamı veren sorgusudur. Özet, kategori, işyeri dağılımı, en büyük
  harcamalar, tek işyeri ve bütçe kontrolü bağlantı alır (`kind = Purchase`, `accountType = Personal`; bütçe hep `ThisMonth`, işyeri
  kategoriden önce gelir); karşılaştırma, aylık seri, düzenli/yaklaşan ödeme ve tasarruf almaz (tek bir satır listesi yok). Okunamayan
  argüman (`Yesterdayy`, tek uçlu tarih) bağlantısız bırakır, cevabı bozmaz; yalnız `Answer` bağlantı taşır. **`Custom` dönemde
  istemci `period` göndermez, yalnız `from` ve `to` gönderir** (arama ikisini birlikte kabul etmez).
- Müşteri **başına** dakikada 10 mesaj (SEC-06, K6-A); aşınca 429 + `Retry-After`. Sınır token'daki müşteriye
  bakar; bu yüzden `UseRateLimiter` kimlik doğrulamanın **arkasındadır** (tokensız istek sayaca girmeden 401 alır).
- Hata eşlemesi (K5-A; iPhone'un yapacağı şeye göre üç kod, AI servisinin kendi kodu yalnız logda):

| AI servisinden gelen / olan | API'nin cevabı |
|---|---|
| 200 ve sözleşmeye uyan gövde | 200 `AssistantReply` |
| 200 ama gövde bozuk, alan eksik ya da `type` tanınmıyor | 502 `assistant_failed` |
| 401 (müşteri tur sırasında silinmiş) | 401 `session_invalid` |
| 503 (`ai_unavailable`, `model_missing`, `backend_unavailable`) ya da bağlantı kurulamıyor | 503 `assistant_unavailable` |
| 504 ya da API'nin kendi süresi doldu | 504 `assistant_timeout` |
| 502 (`ai_answer_rejected`, `ai_bad_tool_call`, `ai_bad_response`), 400, 404, 500 ve diğerleri | 502 `assistant_failed` |
| İstemci bağlantıyı kopardı | cevap yazılmaz; AI servisine giden istek iptal edilir |

### `GET /api/assistant/status`

Sohbet ekranı açılırken çağrılır (K4-C). Her zaman 200 `{ "available": true | false }` (K8-A): AI servisinin
`/health/ready`'si 5 sn içinde 200 dönmezse `false`. Token AI servisine gitmez. Silinmiş müşteri burada da 401
`session_invalid` alır.

### `GET /api/assistant/suggestions` (CHAT-06)

Sohbet ekranı açılırken çağrılır: `{ "suggestions": ["…", "…", "…"] }` (en fazla 3), müşterinin verisine göre modelsiz seçilir
(yaklaşan ödeme, bu ayın harcaması, düzenli ödeme; kurallar docs/local-ai.md). Token AI servisine aynen gider (`GET /v1/suggestions`,
oradan MCP server'a). Hata eşlemesi mesajlarınkiyle aynı (yukarıdaki tablo; `suggestions` eksik/null/null öğe 502); silinmiş müşteri
AI servisine gitmeden 401 `session_invalid`. Model çalışmadığı için dakikada 10 mesaj sınırına **girmez** (açılış soruları mesaj
hakkını yemesin). Ada (demo anı): "Önümüzdeki 7 günde hangi ödemelerim çıkacak?", "Bu ay ne kadar harcadım?", "Aboneliklerim neler?".

### Ayarlar (AI-07)

| Ayar | Varsayılan | Kural |
|---|---|---|
| `BANKINGAI_AI_URL` | `http://127.0.0.1:8000/` (K7-C) | Mutlak http/https adres; sona `/` eklenir. Yanlış değer API'yi açtırmaz. |
| `BANKINGAI_AI_TIMEOUT_SECONDS` | 35 (K4-B; AI servisinin 30 sn'sinin biraz üstü, onun hatası önce gelsin) | 1–600 tam saniye; başka değer API'yi açtırmaz. |

Açılışta logda tek satır: `AI service: http://127.0.0.1:8000/ (chat timeout 35 s)`.

### Kod

`Api/Assistant/`: `AssistantSettings`, `IAssistantClient` + `AssistantClient` (typed `HttpClient`; kendi süresini
`CancellationTokenSource` ile tutar, istemcinin iptalinden ayırır), `AssistantReply` (DTO'lar), `AssistantException`
(`Unavailable` / `Timeout` / `Failed`; `ApiExceptionHandler` 503/504/502'ye çevirir). `Controllers/AssistantController`
(`try/catch` yok), `Security/BearerTokenExtensions` (doğrulanmış token'ı başlıktan alır),
`Application/Customers/ICustomerCheck` (K10-A; kural `CustomerScope`'ta tek yerde). Log (K9-A): mesaj metni ve token
yazılmaz; yalnız tür, tool adları, model, süre, mesaj uzunluğu ve AI servisinin hata kodu.
