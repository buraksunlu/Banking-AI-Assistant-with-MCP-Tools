# Girdi kuralları — VAL-01–VAL-07

Tarih: 9 Ekim 2026 (durum notu 13 Ekim 2026).

Bu belge altı entity için beklenen davranışı ve uygulanan doğrulama sınırlarını
tanımlar. Entity, DbContext, configuration ve migration kontrolleri VAL-07 ile
uygulandı; geçersiz örnekler C# ve gerçek PostgreSQL üzerinde test edildi.
Transaction ekleme ile Account.Balance değişimi BAL-01–04 ile aynı atomik yazma
yolunda birleştirildi (bugünkü adı `Writing/TransactionWriter`; iki bacaklı kayıtta KIND-03).

> Durum notu (13 Ekim 2026): Aşağıdaki tablolardaki "mevcut durum" sütunları VAL-01 (9 Ekim)
> anındaki eksikleri gösterir. Bu eksikler VAL-07 ile kapatıldı (aşağıdaki "VAL-06/VAL-07 uygulama
> doğrulaması"). HTTP girdi kuralları ve hata kodları `docs/api.md`'dedir; ortak hata cevabı API-13'te.

## Ortak davranış

| Girdi | Beklenen davranış | Mevcut durum / sorumluluk |
| --- | --- | --- |
| Zorunlu alanın verilmemesi veya null olması | Reddet; alanı ve nedeni belirt | `required` derleme zamanı zorunluluğudur, runtime kontrolü değildir. DB `NOT NULL` eşlemeleri var; yazma yolu doğrulaması henüz yok |
| Zorunlu metinde `""`, `"   "`, yalnızca tab veya satır sonu | Reddet | C# tarafında `string.IsNullOrWhiteSpace` ile kontrol edilecek; DB kontrollerinin kapsamı aşağıda belirtiliyor |
| İsteğe bağlı metinde null | Kabul et | NormalizedMerchant ve Source nullable |
| İsteğe bağlı metin verilmiş ama boş/whitespace | Reddet; veri yoksa null kullanılmalı | Bu kontrol henüz yok; sessiz trim/null dönüşümü yapılmayacak |
| Maksimum uzunluğu aşan metin | Reddet; sessiz kesme yapma | DB uzunluk eşlemeleri var; uygulamada kaydetmeden önce kontrol eksik |
| Kodlarda yanlış harf büyüklüğü veya kenar boşlukları | Geçerli kodla eşleşmiyorsa reddet; otomatik dönüştürme yapma | Whitelist kontrolü yazma yolunda da yapılacak; DB `char(3)` son boşluk davranışına tek başına güvenilmez |
| Geçersiz kayıt | Kalıcı kayıt veya kısmi yan etki oluşmamalı | Yazma akışında validation ve gerektiğinde DB transaction kullanılacak; bakiye akışı BAL adımlarında |

HTTP katmanı VAL-01 sırasında yoktu; HTTP hata kodları ve cevap sözleşmesi artık `docs/api.md`'dedir
(ortak hata cevabı API-13). DB kısıtları son korumadır; kullanıcıya alan bazlı hata sunmak için
yazma yolunda ayrıca doğrulama gereklidir.

## Kimlikler ve ilişkiler

| Alan | Beklenen davranış | Kaynak koddaki mevcut koruma / eksik |
| --- | --- | --- |
| Customer.Id | Uygulama üretir; Guid.Empty reddedilir | NewGuid varsayılanı var, fakat init ile boş GUID atanabilir; entity guard ve boş GUID CHECK yok |
| Account.Id, Transaction.Id | Uygulama üretir; Guid.Empty reddedilir | NewGuid varsayılanı ve DB CHECK var; entity setter boş GUID'i engellemez |
| Account.CustomerId | Boş GUID veya olmayan Customer reddedilir | Entity guard, DB CHECK ve FK var |
| Transaction.AccountId | Boş GUID veya olmayan Account reddedilir | Entity guard ve DB birleşik FK var; ayrıca para birimi eşleşmesi korunur |
| TransactionCategory.Id, MarketAsset.Id | Sabit seed ID, 1–32767; sıfır/negatif reddedilir | short tipi, DB pozitiflik CHECK ve otomatik üretmeme eşlemesi var; required geçerli ID kontrolü değildir |
| Transaction.CategoryId | null kabul; doluysa pozitif ve mevcut kategori olmalı | Nullable FK var; null kategorize edilmemiş demek. OTHER ayrı bir kategoridir |
| DailyMarketPrice.MarketAssetId | Pozitif ve mevcut MarketAsset olmalı | FK var; hedef tablonun pozitif ID kuralı dolaylı olarak sıfır/negatifi engeller |
| DailyMarketPrice.Id | Yeni nesnede 0 normaldir; kayıtta DB üretir, dış girdiden alınmaz | private set ve identity var. Identity BY DEFAULT, doğrudan SQL ile açık ID verilmesini tek başına yasaklamaz |

## Metin alanları ve uzunluklar

Maksimum uzunluğa eşit değer kabul edilir; bir fazlası reddedilir. Aşağıdaki
uzunluklar PostgreSQL karakter sınırlarıdır. Uygulama doğrulamasında Unicode
karakterler için C# `string.Length` (UTF-16 birimleri) ile farkı dikkate alınmalı;
özellikle emoji içeren sınır örnekleri ayrıca doğrulanmalıdır.

| Alan | Maksimum | Null/boş metin davranışı ve mevcut durum |
| --- | ---: | --- |
| Customer.FirstName, LastName | 100 | Null/whitespace reddedilmeli. NOT NULL var; `btrim` CHECK yalnızca normal boşlukları temizlediği için yalnızca tab/satır sonu girdisini kaçırabilir |
| Customer.CustomerNumber | 32 | DB üretir; `CUST-` + sıfırla başlamayan pozitif tam sayı biçimi ve unique kuralı var; dış girdiden alınmayacak |
| Customer.PreferredCurrency | 3 | Zorunlu; aşağıdaki whitelist geçerli |
| Customer.TimeZone | 100 | Zorunlu; PostgreSQL `pg_timezone_names` içinde bulunan ad kabul edilir. Mevcut DB fonksiyonu bu üyeliği kontrol eder |
| Account.AccountName | 100 | Zorunlu; NOT NULL ve boş olmayan karakter arayan regex CHECK var |
| Account.AccountNumber | 50 | Zorunlu, unique; NOT NULL ve regex CHECK var; gerçek IBAN değil dummy identifier |
| Account.CurrencyCode, Transaction.CurrencyCode | 3 | Zorunlu; aşağıdaki whitelist geçerli |
| Transaction.RawDescription | 500 | Zorunlu; NOT NULL ve regex CHECK var. Ham açıklama korunur; sessiz trim/kısaltma yapılmaz |
| Transaction.NormalizedMerchant | 150 | null kabul; doluysa whitespace olmamalı. DB yalnızca uzunluğu kontrol ediyor |
| TransactionCategory.Code | 50 | Zorunlu, unique; NOT NULL ve regex CHECK var. Sabit kategori listesine üyelik DB CHECK ile korunmuyor; seed/yazma yolunda kontrol edilecek |
| TransactionCategory.DisplayName | 100 | Zorunlu, benzersiz olmak zorunda değil; NOT NULL ve regex CHECK var |
| MarketAsset.Symbol | 30 | Zorunlu, unique; NOT NULL ve regex CHECK var; seed eşleşmesi ayrıca doğrulanacak |
| MarketAsset.Name | 100 | Zorunlu; NOT NULL ve regex CHECK var |
| MarketAsset.AssetType | 30 | Zorunlu; Currency/Commodity CHECK var |
| MarketAsset.BaseUnit | 20 | Zorunlu; NOT NULL ve regex CHECK var; USD/EUR/GRAM üyeliği ve Symbol uyumu DB'de ayrıca denetlenmiyor |
| MarketAsset.QuoteCurrency | 3 | Zorunlu; TRY CHECK var |
| DailyMarketPrice.Source | 100 | null kabul; gerçek import kaynağı dolu ve whitespace olmayan metin sağlamalı. DB'de uzunluk var, boş metin kontrolü yok |

DB regex whitespace tanımı ile .NET whitespace tanımının bütün Unicode
karakterleri için aynı olduğu varsayılmamalı; VAL-07 testleri bu sınırı ele aldı.

## Para birimleri, kodlar ve enum'lar

| Alan | Kabul edilen değerler | Reddedilen örnek / mevcut koruma |
| --- | --- | --- |
| Customer.PreferredCurrency | TRY, USD, EUR | GBP, TL, try reddedilir; DB CHECK var. Entity varsayılanı TRY |
| Account.CurrencyCode | TRY, USD, EUR, GBP | TL, US, usd reddedilir; DB CHECK var |
| Transaction.CurrencyCode | TRY, USD, EUR, GBP; hesabıyla aynı olmalı | TRY hesabına USD işlem reddedilir; CHECK + AccountId/CurrencyCode birleşik FK var |
| Transaction.Direction | Debit=1, Credit=2 | 0, 3, -1 reddedilir; C# enum tek başına bunu engellemez, DB CHECK var |
| Transaction.CategorizationSource | null, Rule=1, Llm=2, Manual=3 | 0 veya 4 reddedilir; DB CHECK var. Diğer kategori alanlarıyla uyum VAL-04'te |
| MarketAsset.AssetType | Currency, Commodity | Stock, Bond, currency reddedilir; DB CHECK var |
| MarketAsset.QuoteCurrency | TRY | USD, EUR, try reddedilir; DB CHECK var |
| MarketAsset.IsActive | true veya false | false geçerli bir değerdir; required yalnızca açık atamayı ister, DB NOT NULL var |
| TransactionCategory.Code | FOOD, GROCERY, CLOTHING, TRANSPORTATION, ENTERTAINMENT, SUBSCRIPTION, TRAVEL, FUEL, BILLS, HEALTH, OTHER | food veya bilinmeyen kod reddedilmeli; DB unique/NotBlank tek başına liste üyeliğini sağlamaz |

Market seed eşleşmeleri `USDTRY/Currency/USD/TRY`, `EURTRY/Currency/EUR/TRY`,
`GRAM_GOLD_TRY/Commodity/GRAM/TRY` olmalıdır. MA-03 ve SEED-05'teki eşleşme
kontrolü gereklidir; yalnızca alanların ayrı ayrı geçerli olması yeterli değildir.

## Confidence ve sayısal sınırlar

| Alan | Beklenen davranış | Mevcut koruma / sonraki karar |
| --- | --- | --- |
| Transaction.CategoryConfidence | null veya 0–1 dahil; 0 ve 1 geçerli, -0.1 ve 1.1 geçersiz | numeric(5,4) ve DB aralık CHECK var; C# setter kontrolü yok |
| CategoryConfidence için hassas sınır | 1.00001 gibi 1'den büyük ham değer reddedilmeli | DB önce 4 ondalığa yuvarlayıp sonra CHECK uygulayabilir; ham girdi aralığı yazma yolunda kontrol edilmeli. VAL-04'e göre dört ondalıkla kayıpsız temsil zorunlu; uygulaması henüz yok |
| Confidence için NaN/sonsuz | Reddet | C# decimal bu değerleri taşımaz; PostgreSQL aralık CHECK/precision koruması var |
| Transaction.Amount | Pozitif, sonlu, iki ondalıkla kayıpsız temsil edilebilir | Entity guard ve DB CHECK var; numeric(18,2). Aşağıdaki VAL-02 precision/aralık kontrolleri henüz kodda yok |
| Account.Balance | Sonlu, yeni hesapta 0 ve bu projede negatif olamaz | private set, C# doğrulaması, DB numeric(18,2), NaN ve `Balance >= 0` CHECK var |
| DailyMarketPrice.Price | Pozitif ve sonlu | DB numeric(20,8) ve CHECK var; entity guard henüz yok. Kaynak hassasiyeti/import dönüşümü ayrıca belirlenecek |

## VAL-02 — Amount ve Balance precision politikası

Kapsam Transaction.Amount ve Account.Balance'tır. İkisinin mevcut EF eşlemesi
`HasPrecision(18, 2)`, DB karşılığı `numeric(18,2)` olarak kalır. Precision toplam
18 basamak, scale ondalık kısımda 2 basamak demektir; tam kısma 16 basamak kalır.
Tutarlar C# tarafında decimal ile taşınır; float/double üzerinden dönüştürülmez.

### Kabul ve ret kararları

| Kural | Karar ve gerekçe |
| --- | --- |
| İki ondalık sınırı | Değer, iki ondalıkla kayıpsız temsil edilebilmeli. Değeri değiştiren yuvarlama veya kesme yapılmaz |
| Sondaki sıfırlar | 12.340 ve 12.3400 kabul edilir; sayısal olarak 12.34 ile aynıdır. Yazımdaki basamak sayısı yerine değer esas alınır |
| Fazla anlamlı ondalık | 12.345, 12.341 ve 0.001 reddedilir; kaydetmek için Round/Truncate uygulanmaz |
| Amount aralığı | 0.01 ile 9999999999999999.99 dahil. Sıfır/negatif Amount reddedilir; yön Direction ile belirlenir |
| Balance temsil aralığı | 0 ile 9999999999999999.99 dahil | Kredili hesap yok; negatif değer iş kuralı ve DB CHECK ile reddedilir |
| Yeni hesap bakiyesi | 0.00; dış girdiden bakiye atanmaz. Açılış fonlaması Credit işlemle temsil edilir |
| Bakiye değişimi | Geçerli Amount ile hesaplanan yeni bakiye de precision ve temsil aralığı kontrolünden geçer. Taşmada işlem ve bakiye değişimi birlikte geri alınır; BAL adımlarında uygulanacak |
| Para birimleri | Mevcut TRY/USD/EUR/GBP kapsamının tamamında bu iki ondalık politikası geçerli. Yeni para birimi eklenirse hassasiyet kararı yeniden incelenir |
| Hata davranışı | Alan ve nedeni bildirilir: örneğin “Amount en fazla iki anlamlı ondalık basamak içerebilir.” Değer sessizce düzeltilmez ve kayıt yapılmaz |

### VAL-07 için kabul örnekleri

Aşağıdaki sonuçlar hedef davranış ve kabul örnekleridir; VAL-07 testlerinde
ilgili C# ve DB kontrolleri çalıştırıldı. SQL/C# gösterimine uygun olarak
ondalık ayırıcı nokta kullanıldı.

| Girdi / işlem | Beklenen sonuç |
| --- | --- |
| Amount = 12, 12.3, 12.34 | Kabul; DB'de sırasıyla 12.00, 12.30, 12.34 |
| Amount = 12.3400 | Kabul; 12.34 ile aynı parasal değer |
| Amount = 12.345 veya 12.341 | Precision hatası; 12.35/12.34'e dönüştürülmez |
| Amount = 0.001 | Reddet; 0.00'a dönüştürülmez |
| Amount = 0 veya -1.00 | Pozitiflik hatası |
| Amount = 0.01 | Kabul; en küçük pozitif tutar |
| Amount = 9999999999999999.99 | Teknik üst sınırda kabul |
| Amount = 10000000000000000.00 | Temsil aralığı hatası |
| Yeni Balance = 0.00 | Kabul |
| Yeni Balance = -12.345 | Negatif bakiye iş kuralı ve precision nedeniyle reddedilir |
| Yeni Balance = -12.34 | Parasal precision açısından iki basamaklı olsa da kredili hesap olmadığı için reddedilir |
| Balance üst sınırdayken Credit 0.01 | Bakiye taşması; işlem ve bakiye birlikte başarısız olmalı |
| Balance = 0.10, Credit = 0.20 | Sonuç tam 0.30; decimal ile hesaplanmalı |

### Kontrol nerede uygulanacak?

Seed, import ve ileride API gibi her yazma yolu, yuvarlanmamış değeri DB'ye
göndermeden önce doğrulamalı. Amount için entity guard'ı; bakiye için kontrollü
güncelleme yolu VAL-07'de temel entity/SaveChanges sınırında ele alındı. Önce temsil aralığı,
ardından değerin iki ondalıkla kayıpsız temsil edilip edilmediği kontrol edilir.
Örneğin decimal değer ile iki ondalığa yuvarlanmış karşılığını karşılaştırmak
doğrulamada kullanılabilir; karşılığı orijinalin yerine atayıp kaydetmek yasaktır.
Kontrol sırasında ölçeksiz decimal.MaxValue değerini 100 ile çarparak taşma
yaratılmamalıdır. Ham metin/JSON decimal'e çevrilirken oluşabilecek hassasiyet
kaybı da giriş sözleşmesinde engellenmeli; parser'ın önceden yuvarladığı değerle
yapılan sonradan kontrol yeterli değildir. API parsing ayrıntısı API adımındadır.

PostgreSQL `numeric(18,2)`, fazla ondalıklı girdiyi scale'e uydurmak için
yuvarlar; 12.345 değeri 12.35 olabilir. Sütundaki CHECK, başlangıçtaki ham değeri
geri getiremez; örneğin aynı sütuna `Amount = round(Amount, 2)` eklemek kaybolan
basamakları tespit etmez. `HasPrecision(18,2)` da C# runtime doğrulaması değildir.
Kaynak: [PostgreSQL 18 numeric açıklaması](https://www.postgresql.org/docs/18/datatype-numeric.html).

Bu nedenle doğrudan SQL ile uygulama doğrulamasını atlayan yazımlarda, mevcut
şema tek başına sessiz yuvarlamayı engellemez. Bu sınır açıkça korunuyor; bu
adımda şema veya migration değiştirilmedi.

DailyMarketPrice.Price'ın numeric(20,8) eşlemesi ve CategoryConfidence'ın
numeric(5,4) eşlemesi bu iki ondalık politikasına dahil değildir. Yatırım
hesaplamalarında ara birimleri iki ondalığa yuvarlama kararı da verilmedi.

Doğrulama: mevcut entity ve configuration kaynakları incelendi. 9 Ekim'deki
salt okunur DB örnek sorgusu bağlantı kurulamadığından çalışmadı; canlı DB testi
başarılı sayılmadı. PostgreSQL yuvarlama davranışı resmî dokümandan doğrulandı.
Bu politika `InputRules` ve `BankingDbContext.SaveChanges` sınırında uygulanır;
geçersiz precision örnekleri testlerde reddedilir.

## VAL-03 — Tarih ve zaman politikası

Bu bölümdeki entity guard'ları, SaveChanges kontrolleri ve DB kısıtları VAL-07
ile uygulandı. UTC, gelecek tarih, default tarih ve geçmiş import örnekleri
test edildi.

### Alanların anlamı

| Alan | Anlam ve beklenen davranış | Mevcut kod |
| --- | --- | --- |
| Transaction.OccurredAt | İşlemin gerçekten gerçekleştiği an; zorunlu, sonlu, UTC. Geçmiş olabilir; gelecek olamaz | required DateTimeOffset ve timestamptz eşlemesi var; default/gelecek tarih guard'ı yok |
| Customer/Account/Transaction.CreatedAt | Bu sistemde kayıt oluşturma zamanı; backend üretir, istemciden alınmaz. Güncellemede ve tekrar importta korunur | Entity oluşturulurken UtcNow varsayılanı var; public init ile değiştirilebilir, backend kontrolü henüz garanti değil |
| Transaction.CategorizedAt | Backend'in kategoriyi atadığı/güncellediği an; null olabilir, doluysa sonlu UTC | Nullable DateTimeOffset ve timestamptz eşlemesi var; C# ve DB'de kategori alanlarıyla birlikte doğrulanır |
| DailyMarketPrice.PriceDate | Kaynağın fiyatı ait saydığı takvim günü; DateOnly. Saat veya offset içermez | date eşlemesi var; default, en büyük tarih ve gelecek gün kontrolü yok |

### UTC, varsayılan değer ve gelecek tarih

| Durum | Karar |
| --- | --- |
| Açık Z veya offset içeren işlem zamanı | Kabul edilebilir; karşılaştırmadan ve entity'ye atamadan önce ToUniversalTime ile aynı anı UTC/offset 0 olarak temsil et |
| Offset içermeyen zaman metni | Reddet; sunucunun yerel saat dilimini tahmin olarak kullanma. Tarihsel kaynağın belgelenmiş saat dilimi varsa import adaptörü açıkça dönüştürebilir |
| Entity/persistence sınırı | DateTimeOffset.Offset sıfır olmalı. Offset 0 olmayan değer entity/yazma yolu sınırında reddedilmeli; dönüşüm giriş/import adaptörünün sorumluluğu |
| Saat dilimi dönüşümünde belirsiz/geçersiz yerel saat | Kaynak offset veya açık çözüm sağlamıyorsa reddet; rastgele offset seçme |
| Zorunlu timestamp eksik/null/default | Reddet. OccurredAt eksikken otomatik UtcNow atama; gerçek işlem zamanını uydurma |
| MinValue/MaxValue ve DB infinity değerleri | Olay zamanlarında ve PriceDate'te reddet; bunlar gerçek olay/piyasa tarihi yerine sentinel olarak kullanılmayacak |
| İşlemin kabul anı | Yazma işleminin başında backend saatinden bir kez referenceUtc al. Aynı yazımdaki kontroller ve yeni CreatedAt için bu referansı kullan |
| OccurredAt > referenceUtc | Reddet; ilk kapsamda tolerans 0. Tarihi şimdiye kırpma. Zamanlanmış/gelecek işlemler bu modelin kapsamında değil |
| OccurredAt == referenceUtc | Kabul; aynı anda oluşturulan işlem geçerli |
| OccurredAt < referenceUtc | Diğer kurallar geçerliyse kabul. Son altı ay/iki yıl sorgu veya veri kapsamı hedefidir; genel girdi yaş sınırı değildir |
| PriceDate | Kaynağın takvim günü korunur. İlk TRY piyasa kapsamı için referenceUtc'nin Europe/Istanbul karşılığındaki günden ileri olmamalı; bugünkü veri yalnızca kaynak gerçekten yayımladıysa alınır |

Npgsql, timestamptz yazımında DateTimeOffset için offset 0 bekler. PostgreSQL
timestamptz orijinal saat dilimi adını/offset'i saklamaz; pgAdmin'deki gösterim
oturum saat dilimine göre değişebilir. Npgsql tarih tiplerinde MinValue/MaxValue
ile infinity eşlemeleri yapabildiği için yalnızca NOT NULL kontrolü yeterli değildir.
Kaynak: [Npgsql tarih/zaman eşlemeleri](https://www.npgsql.org/doc/types/datetime.html).

### Geçmiş import ve synthetic seed

- Geçmiş işlemde OccurredAt kaynağın olay zamanıdır. CreatedAt bu sisteme ilk
  kayıt zamanıdır; iki alan eşit olmak zorunda değildir. Yeni kayıtta
  OccurredAt <= CreatedAt olmalıdır. Tekrar import CreatedAt'ı yenilemez.
- Account.CreatedAt hesabın bu sistemde kayıt zamanıdır; gerçek hesap açılış
  tarihi alanımız yoktur. Tarihsel importu sırf işlem Account.CreatedAt'tan
  eski diye reddetmeyiz. Hesabın mevcut olması ve diğer FK kuralları yine gerekir.
- Tarihsel fiyatlarda PriceDate import günüyle değiştirilmez. Günlük piyasa
  verisi için saat uydurulmaz; eksik günler fiyat üretilerek doldurulmaz.
- Synthetic seed, tekrarlanabilirlik için sabit bir UTC referans kullanabilir;
  bu yalnızca güvenilen seed/test yoluna aittir. Seçilen referans çalıştırma
  anından ileri olmamalı; üretilen OccurredAt referansı aşmamalı. CreatedAt
  için de belgelenmiş sabit seed zamanı kullanılabilir. Normal dış girdiye
  referenceUtc veya CreatedAt belirleme yetkisi verilmez.
- Veri kaynağının tarih biçimi ve saat dilimi import adaptöründe açıkça
  tanımlanır. İşlem anından piyasa günü çıkarılırken Europe/Istanbul'a
  çevrilip tarih alınır; doğrudan UTC tarih kısmı kullanılmaz.

### VAL-07 için örnekler

Referans: 2026-10-09T09:00:00Z. Bu örnekler kabul kriteridir, çalıştırılmış test değildir.

| Girdi | Beklenen sonuç |
| --- | --- |
| OccurredAt = 2026-10-09T12:00:00+03:00 | Girişte 09:00:00Z'ye dönüştürülür; kabul |
| OccurredAt = 2026-10-09T09:00:01Z | Gelecek tarih; reddet |
| OccurredAt = 2026-03-01T10:00:00Z | Geçmiş import kabul; yeni CreatedAt = 2026-10-09T09:00:00Z |
| OccurredAt = 2026-10-09T12:00:00 (offset yok) | Kaynak saat dilimi de belli değilse reddet |
| OccurredAt = default | Reddet; şimdiyle doldurma |
| CategorizedAt = null | Tarih açısından kabul; kategori alanlarıyla uyum VAL-04'e bağlı |
| PriceDate = 2026-10-10 | Referansın Türkiye gününden ileri; reddet |
| PriceDate = default(DateOnly) veya DateOnly.MaxValue | Reddet |
| İşlem anı = 2026-10-08T22:30:00Z | Türkiye takvim günü 2026-10-09; piyasa fiyatı araması bu günü esas alır |

Kontroller, ham tarih yorumlandıktan sonra fakat DB yazımından önce yapılmalı.
Npgsql'nin offset kontrolü iş kuralı doğrulamasının yerine geçmez. Bu adımda
kaynak kod ve resmî sağlayıcı dokümanı incelendi; yeni DB testi veya build yapılmadı.

## VAL-04 — Kategorilendirme alanlarının birlikte tutarlılığı

Bu dört alan, kabul edilerek kaydedilmiş son kategori atamasını temsil eder;
başarısız veya kabul edilmeyen model önerisini saklamak için kullanılmaz.
Aşağıdaki tablo tasarım kararıdır; VAL-07 ile entity metotları, SaveChanges doğrulaması ve DB CHECK
olarak uygulandı.

| Durum | CategoryId | CategorizationSource | CategoryConfidence | CategorizedAt |
| --- | --- | --- | --- | --- |
| Kategorisiz | null | null | null | null |
| Deterministik kuralla atanmış | Mevcut kategori ID'si | Rule (1) | null | Backend tarafından üretilmiş UTC zaman |
| LLM önerisi kabul edilmiş | Mevcut kategori ID'si | Llm (2) | Zorunlu; sonlu, 0–1 dahil | Backend tarafından üretilmiş UTC zaman |
| Manuel atanmış | Mevcut kategori ID'si | Manual (3) | null | Backend tarafından üretilmiş UTC zaman |

Bu dört biçim dışındaki kombinasyonlar reddedilir. Rule ve Manual için
confidence = 1 üretilmez: atama yönteminin deterministik veya insan tarafından
olması yüzde yüz doğruluk kanıtı değildir. CategoryId için mevcut FK geçerlidir;
LLM yeni kategori yaratamaz, backend mevcut Code'u doğrulayıp ID'ye eşler.

### Atama, değiştirme ve kaldırma

- Dört alan aynı yazma işleminde birlikte güncellenir. CategoryId tek başına
  değiştirilip önceki source/confidence/tarih bırakılamaz. Hata olursa eski
  geçerli durum korunur; yarım kategori bilgisi kaydedilmez.
- CategorizedAt, son kabul edilen atamanın backend zamanıdır; VAL-03'teki UTC,
  sonlu tarih ve gelecek olmama kuralları geçerlidir. CreatedAt'tan önce olamaz;
  kayıtla aynı anda atamada eşit olabilir. Geçmiş importta OccurredAt eski olsa
  da bugün yapılan kategorilendirmenin tarihi bugündür.
- LLM sonucunu manuel düzeltirken source Manual, confidence null ve tarih yeni
  atama zamanı olur. Aynı kategori farklı yöntemle yeniden atanırsa yöntem ve
  tarih de yeni atamayı yansıtır. Tekrar denenen aynı başarılı yazım sırf retry
  nedeniyle yeni atama sayılıp zamanını değiştirmemeli; ayrıntı yazma yolunda.
- Kategoriyi açıkça kaldırma işleminde dört alan birlikte null yapılır.
  OTHER atamak kaldırma değildir; OTHER da tabloya uygun source/tarih gerektirir.
- Model hatası, bilinmeyen kod veya kabul edilmeyen öneri mevcut kategoriyi
  silmez. İlk atamada kabul edilmiş sonuç yoksa işlem dört alanı da null kalır.
- Otomatik akış manuel atamayı kendiliğinden ezmemeli. Eşzamanlı yeniden
  kategorilendirmede yalnızca atomik yazımın yeterli olmadığı dikkate alınacak;
  beklenen önceki durumla güncelleme gibi koruma CAT-06 uygulamasında ele alınacak.

### Confidence ve LLM kabul eşiği

- Confidence aralığı 0–1 dahildir; 0/1 sınır değerleri veri modeli açısından
  geçerlidir. Model skoru kalibre edilmiş doğruluk olasılığı varsayılmaz.
- Mevcut numeric(5,4) ile kayıpsız temsil edilebilen skor kullanılır: 0.85000
  kabul edilebilir, 0.85001 precision hatasıdır. Ham skor aralık/precision
  kontrolünden geçmeden yuvarlanıp eşikle karşılaştırılmaz. Bu, DB'nin sessiz
  yuvarlamasının kabul kararını değiştirmesini önler; kontrol henüz kodda yoktur.
- Otomatik atama için uygulama katmanında yapılandırılmış bir eşik kullanılacak.
  Eşik değeri burada uydurulmadı; CAT-05'te değerlendirme örnekleriyle seçilecek.
  Seçildiğinde confidence >= eşik koşulu kullanılacak; geçerli kategori ve
  diğer girdi kuralları da sağlanmalı. Eşik belirlenmeden otomatik atama açılmaz.
- Düşük skor kategorisiz işlemde dört alanın null kalmasına yol açar; mevcut
  kategori varsa korunur. Düşük skor nedeniyle otomatik OTHER atanmaz. OTHER,
  değerlendirilip diğer kategorilere uymayan işlem anlamını korur.
- DB'ye yalnızca alan kombinasyonu ve genel aralık kuralları konacak; belirli
  bir LLM kabul eşiği yazılmayacak. Eşiğin değişmesi migration gerektirmemeli;
  daha önce kabul edilmiş kayıtları kendiliğinden geçersiz kılmamalı.

### Geçersiz örnekler ve mevcut eksikler

| Örnek | Beklenen davranış |
| --- | --- |
| CategoryId null, source Llm, confidence 0.9 | Reddet; kabul edilmemiş öneri kategori alanlarında tutulamaz |
| CategoryId dolu, source veya CategorizedAt null | Reddet; atamanın yöntemi ve zamanı eksik |
| CategoryId dolu, source Llm, confidence null | Reddet; LLM atamasının skoru eksik |
| CategoryId dolu, source Rule/Manual, confidence 1 | Reddet; bu yöntemler için confidence null olmalı |
| CategoryId dolu, source Llm, confidence -0.1 veya 1.1 | Reddet; aralık dışı |
| CategoryId dolu, source 0 veya 4 | Reddet; tanımsız enum |
| CategoryId dolu, geçerli source/skor, tarih default veya gelecekte | Reddet; VAL-03 tarih kuralları |
| Llm ataması manuel düzeltilmiş ama eski confidence kalmış | Reddet; dört alan birlikte değiştirilmeliydi |

Mevcut nullable FK, source enum CHECK ve confidence aralık CHECK ayrı ayrı
koruma sağlar; alanların birlikte tutarlılığını sağlamaz. Entity'deki public
setter'lar da geçersiz kombinasyon kurulmasına izin verir. VAL-07'de ortak
yazma doğrulaması ve DB kombinasyon CHECK'i eklendi; SQL'in NULL davranışı
nedeniyle CHECK'in UNKNOWN sonucuyla geçersiz satır kabul etmemesine dikkat
edilecek. Mevcut kayıtlar taranıp uyuşmazlık varsa sessizce tarih/source
uydurulmadan raporlanacak. Uygulama ayrı incremental migration ile ilerleyecek.

Bu bölümdeki örnekler tasarım kabul kriterleridir; CategoryConfidence, tarih ve
atama akışı VAL-07 testlerinde çalıştırıldı. Yeni AI kodu veya tablo eklenmedi.

## VAL-05 — Dataset'in harcama kapsamı (KIND-05 ile güncellendi, 10 Ekim 2026)

İlk sürüm (9 Ekim) transfer üretmiyor ve harcamayı Debit toplamı sayıyordu; o zaman
Direction'dan başka ayırt edici alan yoktu. KIND-01–04 ile `Transactions.Kind`,
`PairId` ve `Accounts.AccountType` eklendi. Bu bölüm güncel kuraldır: **yön (Direction)
paranın nereye gittiğini, tür (Kind) neden gittiğini söyler; harcama türe göre ölçülür.**

### Hareketler ve harcamaya etkisi

| Hareket | Dataset | Direction / Kind | PairId | Harcama toplamına etkisi |
| --- | --- | --- | --- | --- |
| Yeme-içme, market, giyim, ulaşım, eğlence, seyahat, yakıt, sağlık; abonelik ve fatura | Dahil; tamamlanmış ödeme | Debit / Purchase | Banka dışı merchant'ta null | Amount eklenir |
| Kahve Durağı gibi iç dükkândan alışveriş (alıcı tarafı) | Dahil (KIND-04b) | Debit / Purchase | Dolu | Amount eklenir; banka dışı alışverişten farkı yoktur |
| OTHER ile sınıflandırılmış veya kategorisi henüz belirlenmemiş Purchase | Dahil | Debit / Purchase | — | Genel toplama eklenir; kategorisiz olan dağılımda ayrı grupta gösterilir |
| Dükkânın satış geliri (satıcı tarafı) | Dahil; yalnız Business hesaba | Credit / Sale | Dolu | Harcama değildir; ayrı "satış geliri" analizi |
| Müşteriler arası veya kendi hesapları arası transfer | Dahil (KIND-04b); aynı para birimi | Debit veya Credit / Transfer | Dolu | **Harcamaya eklenmez**; gönderen tarafı Debit olsa da alışveriş değildir |
| Maaş | Dahil | Credit / Salary | null | Harcamadan düşülmez |
| Açılış fonlaması | Dahil | Credit / OpeningFunding | null | Harcamadan düşülmez; karşı Debit yoktur |
| Nakit çekme/yatırma | Hariç | Üretilmez | — | Nakit çekmek harcamanın gerçekleştiğini kanıtlamaz |
| Döviz/altın alım-satımı, farklı para birimli transfer, yatırım hesabına aktarım | Hariç | Üretilmez | — | Tarihsel karşılaştırma gerçek yatırım işlemi değildir |
| İade, kısmi iade, iptal/ters kayıt, chargeback | Hariç | Üretilmez | — | Net harcama henüz desteklenmez |
| Kredi kullanımı/ödemesi, kart borcu ödemesi, faiz ve banka komisyonları | Hariç | Üretilmez | — | Mal/hizmet harcamasıyla karıştırılmaz |
| Bekleyen provizyon, başarısız veya planlanmış işlem | Hariç | Üretilmez | — | Durum alanı yok; yalnız tamamlanmış hareket yazılır |

Açılış fonlaması demo için dış kaynaklı bir başlangıç girişidir; gerçek dünyadaki
bütün Credit hareketlerinin gelir olduğu anlamına gelmez. İç dükkân ödemesinin anında
ve komisyonsuz satıcı hesabına geçmesi bir demo basitleştirmesidir.

### Analiz sözleşmesi

- **Harcama = `Kind = Purchase`** kayıtlarının, müşteri/hesap + OccurredAt dönemi + tek
  para birimi kapsamındaki Amount toplamıdır. Kategori filtresi varsa yalnız seçilen
  kategori dahil edilir. Dönem CreatedAt'a göre seçilmez.
- **Bütün Debit'leri toplamak yanlıştır.** Örnek: Ada'nın 1–10 Ekim 2026 harcaması
  735 TRY'dir; Debit toplamı 1.500 TRY transfer nedeniyle 2.235 TRY çıkar.
- Toplam brüt mal/hizmet harcamasıdır; Credit çıkarılarak net harcama üretilmez.
  1000 TRY maaş ve 100 TRY alışverişte harcama 100 TRY'dir; bakiye ile harcama farklıdır.
- Kategorisiz Purchase harcamadan düşürülmez; merchant bulunmaması da kaydı genel
  toplamdan çıkarmaz. Kategori yalnız Purchase'ta bulunur (DB `CK_Transactions_Kind_Category`).
- Kişisel harcama analizleri varsayılan olarak Personal hesapları kapsar. Business
  hesabın satış geliri ayrı sorudur ("dükkânıma ne kadar girdi?" = Sale toplamı).
- Transfer analizleri karşı tarafı PairId üzerinden karşı bacağın hesabından bulur;
  isim veritabanına kopyalanmaz ve maskeli gösterilir.
- TRY, USD, EUR ve GBP birbirine dönüştürülmeden toplanmaz.
- Yatırım karşılaştırması aynı harcama tanımının (Purchase) TRY alt kümesini kullanır.
  Maaş, açılış fonlaması, satış geliri veya transfer "harcasaydın" karşılaştırmasına girmez.

### Bu kapsam nasıl korunuyor?

| Koruma | Yer |
| --- | --- |
| Kind ↔ Direction ↔ PairId ↔ kategori uyumu | `InputRules.Kind`, `EntityValidation` ve 5 DB CHECK (KIND-02) |
| Bir çiftte en fazla bir Debit ve bir Credit | `UNIQUE (PairId, Direction)` |
| Çiftin iki bacağı birlikte, aynı tutar/para birimi/zamanla yazılır | `PairedTransactionWriter` (KIND-03; API-11'e kadar `PairedTransactionSeedWriter`) |
| Yarım çift yazılamaz | Tek bacaklı yazıcı PairId'li kaydı reddeder |
| Sale yalnız Business hesaba | İki bacaklı yazıcı (KIND-04a) ve `CK_Accounts_AccountType` |
| Seed harcama/bakiye beklentileri | Bağımsız fixture testleri (`SyntheticDatasetIntegrityTests`) |

İşlem türü yazma anında açıkça verilir. RawDescription içinde "transfer" aramak veya
LLM'ye tür tahmin ettirmek kapsam koruması olarak kullanılmaz; LLM tür belirleyemez,
yalnız backend'in döndürdüğü türü açıklar.

### İade ve net harcama için ayrı karar

Refund ve net harcama gereksinimi yoktur. İstenirse yeni bir Kind, orijinal işleme
bağlantı, kısmi/çoklu iadeler, para birimi, iade dönemi ve çift sayımı önleme birlikte
tasarlanır. Bu karar alınmadan Credit'leri iade saymak veya Amount'u negatif yapmak yasaktır.

### Seed/analytics kabul örnekleri

| Örnek | Beklenen sonuç |
| --- | --- |
| 1000 TRY açılış + 100 TRY yemek Purchase + 50 TRY abonelik Purchase | Harcama 150 TRY, bakiye 850 TRY |
| Yukarıdaki 50 TRY Purchase kategorisiz | Genel harcama yine 150 TRY; kategorisiz grup 50 TRY |
| 100 TRY Purchase + 500 TRY gönderilen Transfer | Harcama 100 TRY; transfer ayrı raporlanır |
| 120 TRY iç dükkân alışverişi | Alıcının harcaması 120 TRY; dükkânın satış geliri 120 TRY; ikisi birbirini götürmez |
| 100 TRY Debit ve 10 USD Debit | Ayrı toplamlar; 110 diye birleşik harcama verilmez |
| Mart işlemi ekimde import edilmiş | Mart harcamasına dahil; CreatedAt nedeniyle ekime taşınmaz |
| Seed senaryosuna refund, nakit veya döviz transferi eklenmesi | Kapsam ihlali; harcama toplamına sokulmaz |

## Sonraki adımlara bırakılanlar

- VAL-02 politikası yukarıda tanımlandı ve C# SaveChanges/DB sınırında uygulanıp test edildi.
- VAL-03 politikası yukarıda tanımlandı ve C# SaveChanges/DB sınırında uygulanıp test edildi.
- VAL-04 politikası yukarıda tanımlandı ve C# setter/SaveChanges/DB CHECK sınırında uygulanıp test edildi; LLM eşiği CAT-05'te.
- VAL-05 kapsamı KIND-05 ile güncellendi (harcama = Kind Purchase); seed/yazma tarafı KIND-02–04 ile uygulandı, analytics ANA adımlarında.
- VAL-06: kredili hesap yok; negatif bakiye ve yetersiz bakiye reddi kararı.
- VAL-07: entity, SaveChanges ve DB doğrulama sınırları uygulandı; geçersiz örnek testleri geçti.

Atomik bakiye yazma ve eşzamanlılık BAL-01–04'te tamamlandı. Dış API sözleşmesi API adımlarında
ilerliyor (`docs/api.md`); işlem ekleme API-11'de.

## VAL-06/VAL-07 uygulama doğrulaması

- Kredili hesap simülasyonu yoktur. `Account.Balance < 0` C# doğrulaması ve
  `CK_Accounts_Balance_NonNegative` DB CHECK'i ile reddedilir. Seed yazma yolu
  `TransactionWriter` (eski adı `TransactionSeedWriter`) işlemi ve bakiye farkını aynı transaction'a alır;
  bakiye DB'de atomik artırılıp/azaltılır, yetersiz Debit reddedilir. Eşzamanlılık
  ve hata rollback kabul testleri BAL-03/04 kapsamında PostgreSQL'de geçti.
- `InputRules` para precision, metin, UTC tarih ve kategori kombinasyonlarını
  düzeltme yapmadan reddeder. `EntityValidation` altı entity için ortak sınırı
  uygular. `BankingDbContext.SaveChanges/SaveChangesAsync` bu kontrolü yazma
  öncesinde çalıştırır.
- `EnforceInputValidation` migration'ı aktif DB'ye uygulanmıştır. C# unit ve
  PostgreSQL integration testleri toplam 12/12 geçti; deneme kayıtları rollback
  edildi. Testler `.env` değişkenleri yüklenerek çalıştırılmalıdır.

## BAL-03/BAL-04 atomik bakiye yazma kabul testleri

`tests/BankingAI.Data.Tests/TransactionWriterTests.cs` (API-11'e kadar `AtomicTransactionSeedWriterTests.cs`) gerçek PostgreSQL
üzerinde iki senaryoyu doğrular. Sıfır başlangıç bakiyesine önce 100 TRY Credit,
ardından eşzamanlı 25 TRY Credit ve 40 TRY Debit uygulandı. Sonuç
`0 + 100 + 25 - 40 = 85 TRY` oldu; test bunu saklanan işlem kayıtlarından
hesaplayıp Account.Balance ile karşılaştırır.

İkinci senaryoda geçersiz Category FK'si transaction insert'ini reddetti ve
bakiye değişmedi. Ardından 100 TRY bakiyeden 100.01 TRY Debit denendi; insert
önce başarılı olmasına rağmen bakiye güncellemesi reddedildi, transaction geri
alındı ve bakiye 100 TRY kaldı. Teste özel hesap ve hareketler sonunda silindi.

## KIND-01 — İşlem türü ve çift kayıt kararı (10 Ekim 2026)

Kullanıcı kararıyla Transactions iki alan kazanır. Direction paranın yönüdür;
`Kind` hareketin o hesap açısından ekonomik anlamıdır.

| Kind | Değer | Direction | Kategori | PairId | Harcamaya dahil |
| --- | ---: | --- | --- | --- | --- |
| Purchase | 1 | Debit | Alabilir | Banka dışı alışverişte null; iç dükkân alışverişinde dolu | Evet, yalnız bu tür |
| Sale | 2 | Credit | Null | Zorunlu | Hayır |
| Transfer | 3 | Debit veya Credit | Null | Zorunlu | Hayır |
| Salary | 4 | Credit | Null | Null | Hayır |
| OpeningFunding | 5 | Credit | Null | Null | Hayır |

- `PairId` nullable uuid'dir; ayrı Transfers tablosu açılmaz. Aynı çifte en fazla bir
  Debit ve bir Credit girebilir: `UNIQUE (PairId, Direction)`.
- Çiftin iki bacağı aynı tutar ve para birimini taşır, farklı hesaplara yazılır ve tek
  DB transaction'ında oluşur. DB bu "iki bacak birlikte var" kuralını tek başına
  zorlayamaz; iki bacaklı yazıcı (KIND-03) ve entegrasyon testleri korur.
- Kendi hesapları arası transfere izin verilir; para birimi aynı olmalıdır. Döviz
  dönüşümlü transfer, iade/iptal ve komisyon kapsam dışıdır.
- Karşı taraf adı veritabanına kopyalanmaz; sorgu anında karşı hesaptan okunur ve
  maskeli gösterilir (örnek: `Deniz M***`). LLM'e tam müşteri adı verilmez.
- Mevcut kayıtların türü migration'da doldurulur: Debit → Purchase; açıklaması
  "açılış fonlaması" olan Credit → OpeningFunding; "maaş ödemesi" olan Credit → Salary.
  Eşleşmeyen kayıt kalırsa migration hata verip durur; tahmini tür atanmaz.
- LLM/MCP transfer veya ödeme yazamaz; yazma yalnız uygulama/API ve seed üzerindendir.
- VAL-05 harcama tanımı KIND-05'te güncellendi: harcama = `Kind = Purchase` toplamı.
