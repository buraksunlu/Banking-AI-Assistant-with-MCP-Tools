# Girdi kuralları — VAL-01

Tarih: 9 Ekim 2026.

Bu belge altı entity için beklenen davranışı tanımlar. Mevcut durum, entity,
configuration, DbContext ve migration kaynak kodlarının incelenmesine dayanır;
bu adımda canlı DB testleri yeniden çalıştırılmadı. Yeni doğrulama kodu veya
migration eklenmedi. Eksik kontroller VAL-07 kapsamında uygulanacak.

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

HTTP katmanı henüz yoktur. HTTP hata kodları ve cevap sözleşmesi API adımlarında
tanımlanacak. DB kısıtları son korumadır; kullanıcıya alan bazlı hata sunmak için
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
karakterleri için aynı olduğu varsayılmamalı; VAL-07 testleri bu sınırı ele almalı.

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
| CategoryConfidence için hassas sınır | 1.00001 gibi 1'den büyük ham değer reddedilmeli | DB önce 4 ondalığa yuvarlayıp sonra CHECK uygulayabilir; ham girdi aralığı yazma yolunda kontrol edilmeli. Fazla ondalık politikası ayrıca netleştirilecek |
| Confidence için NaN/sonsuz | Reddet | C# decimal bu değerleri taşımaz; PostgreSQL aralık CHECK/precision koruması var |
| Transaction.Amount | Pozitif ve sonlu | Entity guard ve DB CHECK var; numeric(18,2). Fazla ondalık davranışı VAL-02'de |
| Account.Balance | Sonlu; yeni hesapta 0 | private set, DB numeric(18,2) ve NaN CHECK var. Negatif bakiye kararı VAL-06'da; burada yeni yasak tanımlanmadı |
| DailyMarketPrice.Price | Pozitif ve sonlu | DB numeric(20,8) ve CHECK var; entity guard henüz yok. Kaynak hassasiyeti/import dönüşümü ayrıca belirlenecek |

## Sonraki adımlara bırakılanlar

- VAL-02: para precision ve fazla ondalıklı giriş politikası.
- VAL-03: UTC, varsayılan tarih, gelecek/geçmiş tarih davranışı.
- VAL-04: CategoryId, source, confidence ve CategorizedAt birlikte tutarlılık.
- VAL-05: harcama, transfer ve iade kapsamı.
- VAL-06: negatif bakiye politikası.
- VAL-07: bu belgede eksik görünen kontrollerin uygulanması ve geçersiz girdi testleri.

VAL-01'in tamamlanması bu kontrollerin hepsinin kodda uygulanmış olduğu anlamına gelmez.
