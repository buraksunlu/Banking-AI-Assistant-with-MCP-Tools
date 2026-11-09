# Günlük TL fiyatları — kaynak incelemesi

İnceleme tarihi: 10 Ekim 2026. Bu belge kaynak araştırmasıdır; import henüz uygulanmadı.

> Durum (9 Kasım 2026): USD/EUR TCMB'den iki yıllık olarak DB'de (IMP-a, aşağıda "USD/EUR import").
> Kullanıcı kararıyla varlık başına günde **tek değer** saklanır (TCMB döviz alış); 10 Ekim'deki alış/satış
> çifti kararı kaldırıldı. Altın (SRC-02) ayrı adımda: önce resmî kaynak denenir.
> Market kolu API/analytics/AI işlerini engellemez; yalnız opportunity-cost (todo bölüm 13) bunu bekler.

## Saklanacak değer

| Symbol | Price anlamı |
| --- | --- |
| USDTRY | 1 USD karşılığı TRY |
| EURTRY | 1 EUR karşılığı TRY |
| GRAM_GOLD_TRY | 1 gram altın karşılığı TRY |

MarketAsset varlığı, DailyMarketPrice ise kaynak tarihini ve o tarihin fiyatını tutar.
Kullanıcının ilk altın isteği son bir yıldır; todo'daki iki yıllık nihai hedef
korunur. Bir yıllık kontrol iki yıllık hedefi tamamlamaz.

## USD ve EUR: TCMB günlük XML adayı

Resmî kurum kaynağı: https://www.tcmb.gov.tr/kurlar/

Tarih endpoint'i: `https://www.tcmb.gov.tr/kurlar/YYYYMM/DDMMYYYY.xml`.
Üç örnek HTTPS GET ile ücret veya API anahtarı olmadan alındı ve XML olarak
ayrıştırıldı. Bu erişim EVDS API'sinden farklıdır; EVDS anahtarı kullanılmadı.
Yeniden dağıtım şartları ve servis sürekliliği garantisi bu denemeyle doğrulanmış olmaz.

Gerçek yanıtların ilgili alanları:

| Kaynak günü | Kod | Unit | ForexBuying | ForexSelling |
| --- | --- | --- | --- | --- |
| 2024-10-10 | USD | 1 | 34.1522 | 34.2137 |
| 2024-10-10 | EUR | 1 | 37.3463 | 37.4136 |
| 2025-10-10 | USD | 1 | 41.6388 | 41.7138 |
| 2025-10-10 | EUR | 1 | 48.1872 | 48.2740 |
| 2026-10-09 | USD | 1 | 49.1274 | 49.2160 |
| 2026-10-09 | EUR | 1 | 55.1112 | 55.2105 |

Örnek bağlantılar:
- https://www.tcmb.gov.tr/kurlar/202410/10102024.xml
- https://www.tcmb.gov.tr/kurlar/202510/10102025.xml
- https://www.tcmb.gov.tr/kurlar/202610/09102026.xml

İki yıllık aralığın farklı noktaları erişilebilir; aradaki bütün yayın günlerinin
eksiksiz olduğu henüz doğrulanmadı. SRC-01 bu nedenle açık kalır.
10 Ekim kararı: başlangıç alımında ForexSelling, değerleme satışında
ForexBuying kullanılacak; bu yön sağlayıcı tanımıyla doğrulanacak. Bunlar gerçek müşteri işlem fiyatı olarak sunulmayacak.
Unit kontrol edilmeli; kodu yanlış varlığa yazma veya eksik fiyatı sıfır yapma yok.

## Altın: Gold-api.com adayı

- Özel sağlayıcı; resmî kurum değildir.
- https://gold-api.com/pricing: ücretsiz geçmiş/OHLC erişimi saatte 10 istek.
- https://gold-api.com/llms.txt: geçmiş veride `x-api-key` başlığı gerekli.
- `/history` parametreleri: `symbol=XAU`, `startTimestamp`, `endTimestamp`,
  `groupBy=day`, `aggregation=avg`, `orderBy=asc`.
- Tarihler Unix timestamp olarak gönderilir. `avg` günlük ortalamadır, kapanış değildir.
- Belgelenen para birimleri arasında TRY yoktur.

### Gerçek erişim denemesi — 10 Ekim 2026

Yerel GOLD_API_KEY ile iki GET isteği yapıldı; ikisi de HTTP 200 döndü.
Anahtar çıktıya veya bu belgeye yazılmadı.

| İstenen aralık (bitiş hariç) | Dönen günlük satır |
| --- | --- |
| 2025-10-10 → 2026-10-10 | 365 |
| 2024-10-10 → 2026-10-10 | 699 |

İki yıllık yanıtta yinelenen gün, aralık dışı gün ve geçersiz/pozitif olmayan
fiyat bulunmadı. 730 takvim gününün 31'i yok; 178 hafta sonu satırı var.
Dolayısıyla eksikleri yalnızca hafta sonu olarak açıklayamayız. Hafta sonu
fiyatlarının taşınmış değer mi, yeni gözlem mi olduğu henüz bilinmiyor.

Gerçek yanıt örneği:

```json
{"avg_price":"3991.712202","day":"2025-10-10 00:00:00"}
```

Fiyat JSON string olarak geliyor; invariant decimal ile okunmalı.
Gün alanı offset içermiyor. Son gözlem 2026-10-09, avg_price 4186.934422.
Bunlar XAU kaynağının ham değerleridir; gram/TL fiyatı değildir.
Erişim doğrulandı, finansal doğruluk ve kaynak sözleşmesi henüz doğrulanmadı.
Günlük zaman dilimi, XAU birimi, saflık, spot/vadeli niteliği ve bağımsız
fiyat karşılaştırması tamamlanmadan SRC-02 kapatılmayacak.

USD/troy ons tanımı doğrulanırsa olası dönüşüm:

`gram TRY = USD/troy ons × aynı kaynak gününün USDTRY fiyatı / 31.1034768`

Bu türetilmiş referans fiyat olur; kuyumcu alış/satış fiyatı değildir.
Kaynakların saatleri farklı olabilir. Eksik kur günleri, seri seçimi ve dönüşüm
yuvarlaması import öncesi kararlaştırılacak; günler sessizce eşleştirilmeyecek.

## Sonraki küçük adım

Anahtar yerel `.env` içinde hazırdır; uygulama komutları henüz bu değişkeni
okumaz. Sonraki adım kaynak birimini, fiyat tanımını ve eksik günleri doğrulamaktır.
Ardından parser/import ayrı küçük adımlarla hazırlanır.

Bu inceleme DB'yi, seed'i, migration'ları veya uygulama komutlarını değiştirmedi.

## Yerel ham veri indirmesi — 10 Ekim 2026

Kullanıcının onayıyla 2025-10-10 dahil, 2026-10-10 hariç aralık indirildi.
Konum: `data/raw/market/2025-10-10_2026-10-09/`.

- `gold-xau.json`: sağlayıcının değiştirilmeden saklanan 365 günlük XAU yanıtı.
- `tcmb/`: 248 günün ham XML yanıtı; her birinde USD ve EUR alış/satış alanları var.
- `download-report.json`: istek aralığı, altın istek URL'si (anahtarsız), gün bazlı
  TCMB durumları ve ayrıştırılan kur alanları.
- 261 hafta içi gün istendi; 13 gün HTTP 404 döndü. Bu günler otomatik tatil
  sayılmadı. Hafta sonları TCMB'den istenmedi; eksik güne fiyat üretilmedi.
- Altın günlerinin benzersizliği, fiyatların pozitif/sonlu oluşu ve TCMB
  USD/EUR Unit=1 değerleri kontrol edildi. Bu kontroller ekonomik doğruluğu kanıtlamaz.
- Ham indirmeler `.gitignore` ile yerelde tutulur; kaynak verisinin tamamı
  yeniden dağıtılmıyor. Anahtar dosyalara veya rapora eklenmedi.

Bu işlem uygulamaya import komutu eklemez ve DB'ye kayıt yazmaz. Gram/TL dönüşümü,
kaynak tanımı ve eksik gün politikası henüz tamamlanmadı. İki yıllık döviz kapsamı
ve SRC/IMP görevleri tamamlandı olarak işaretlenmedi.

## Güncel kararın kaynak seçimine etkisi — 10 Ekim 2026

Toplam harcama dönem başlangıcında tek seferde yatırılmış varsayılacak.
Başlangıçta sağlayıcı satış, değerlemede sağlayıcı alış fiyatı gerekir.
Yukarıdaki ortalama XAU indirmesi araştırma verisidir; bu iki fiyatı sağlamaz.
Ortalama USD/ons değerini TRY/grama çevirmek alış/satış çiftini üretmez.
Geçmiş ve güncel gram/TL alış–satış kaynağı doğrulanmadan altın import'u
uygulanmayacak; ortalamadan makas türetilmeyecek. Mevcut tek Price şeması
MP-06–08 adımlarında ayrıca tasarlanıp değiştirilecek.

## Adım 2 — alış/satış kaynak incelemesi

### Döviz sonucu

TCMB resmî döviz sayfası bu serileri gösterge niteliğinde tanımlar:
https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB%2BTR/Main%2BMenu/Istatistikler/Doviz%2BKurlari

İndirilen XML'deki ForexSelling başlangıç alımı, ForexBuying değerleme satışı
için kullanılabilir; Banknote alanlarıyla karıştırılmaz. Bu bir referans
simülasyonudur, müşterinin bankasının işlem makası değildir. Bir yıllık
248 günlük çift elde mevcut; iki yıllık döviz kapsamı henüz doğrulanmadı.

### Altın adayları

| Aday | Belgelenen imkân | Sonuç |
| --- | --- | --- |
| Gold-api.com | Geçmiş XAU ortalaması | Eldeki veri alış/satış gereksinimini karşılamıyor |
| Hesaplı Altın | Gram/TL geçmiş satış, güncel alış/satış; ücretsiz anahtar | En yakın aday; gerçek istek HTTP 403, kapsam doğrulanmadı |
| Altın Grafiği | Güncel alış/satış; geçmiş sunulmadığı belirtiliyor | Bir yıllık başlangıç verisi için yeterli değil |
| Gramvey | Belgede en uzun tarih seçimi son ay; repo arşivlenmiş | Bir yıllık erişim doğrulanmadı |

Kaynak belgeleri:
- https://hesaplialtin.com/altin-fiyati-api/gecmis
- https://hesaplialtin.com/altin-fiyati-api
- https://hesaplialtin.com/nasil-hesapliyoruz
- https://altingrafigi.com/ucretsiz-altin-fiyatlari-api
- https://github.com/ahmetilhn/altin-fiyatlari-api

Hesaplı Altın geçmiş endpoint'i:
`/api/v1/public-history?kod=GRAM_ALTIN_TRY&aralik=1y`.
Belgeye göre seri [Unix milisaniye, satış] çiftlerinden oluşur; uzun
aralıklar İstanbul günü kapanışıdır, en fazla 400 nokta döner. `tumu`
seçeneği iki yıllık eksiksiz günlük kapsamın kanıtı değildir.
Güncel `/api/v1/public-prices` içindeki `alis` bozdurma, `satis` alım
fiyatıdır. Kaynak özel sağlayıcıdır; piyasa fiyatlarından hesapladığı
bilgi amaçlı değerleri sunar. Gram ürününün saflığı ayrıca teyit edilmeli.

Belgelerde 13 Ekim'e kadar anahtarsız denemeye izin verildiği yazmasına
rağmen bu ortamda geçmiş isteği 403 döndü. Nedeninin anahtar mı erişim
katmanı mı olduğu kanıtlanmadı; erişim engeli aşılmaya çalışılmadı.
Gold-api.com anahtarı bu başka sağlayıcıya gönderilmedi.
Ücretsiz anahtar sayfası: https://hesaplialtin.com/altin-fiyati-api/anahtar
Anahtar edinmek/başvuru göndermek bu incelemede yapılmadı.
Anahtar başına 60 istek/dakika ve 5 saniyeden sık sorgulamama koşulu
belgelenmiş. Kaynak bağlantısı şartı geçmiş ve ana API belgelerinde
farklı ifade ediliyor; kesin kullanım sözleşmesi açıklığa kavuşturulmalı.

### Kapsam ayrımı

Kullanıcının “geçmişte al, bugün sat” örneği için geçmiş SATIŞ ve güncel
ALIŞ yeterlidir. Her eski günün alış fiyatı bu tek senaryoda zorunlu değildir.
Ancak geçmiş bir günde değerleme veya geriye dönük aynı sonucu üretme
için o günün ALIŞ fiyatı da gerekir. Bu ayrım model tasarımında açıklanacak;
eksik geçmiş alış fiyatı satıştan/ortalamadan uydurulmayacak.

Araştırma sonucu altın kaynağı henüz kesinleşmedi. SRC-02 açık, model/migration/
import değişmedi. Sonraki doğrulama gerçek yetkili yanıt, tarih kapsamı ve
ürün saflığıdır. Başarılı erişim olmadan tablo değişikliğine geçilmez.

### Yeni anahtarla erişim denemesi

HESAPLI_ALTIN_API_KEY yerel ortamdan okunarak X-API-Key başlığında yalnızca
hesaplialtin.com adresine gönderildi. Geçmiş `public-history` ve güncel
`public-prices` isteklerinin ikisi de HTTP 403 döndü. Hata yanıtındaki
`detail`, site sahibinin tarayıcı imzasına göre erişimi engellediğini bildiriyor.
Bu sonuç anahtarın geçersiz olduğunu kanıtlamaz; kimlik doğrulamasının
başarıyla tamamlandığı da doğrulanamadı. Engeli aşmak için istemci taklidi
yapılmadı. Fiyat alınamadı, DB değişmedi; SRC-02 açık kalır.
Sonraki adım sağlayıcının izin verdiği istemci/erişim yönteminin netleşmesidir.

### Alternatif erişim kontrolü — 10 Ekim 2026

- TLcevir: https://tlcevir.com/api-dokumantasyon anahtarsız, kişisel/ticari
  olmayan kullanım için ücretsiz erişim belgeliyor.
  `https://tlcevir.com/api/history/XAU_GRAM?days=365` gerçek isteği başarılı;
  buy/sell alanları var fakat yalnızca 96 satır (2026-07-07–2026-10-10) döndü.
  `/api/rates` isteği de başarılı. Bir yıllık başlangıç fiyatını sağlamıyor.
  Ticari kullanım, saflık ve gün tanımı ayrıca doğrulanmalı.
- Standard Bullion: https://standardbullion.com/gold-price-api ücretsiz,
  kaynak atıflı USD/troy ons verisi belgeliyor.
  `/api/v1/market/history?metal=XAU&range=1y` isteği başarılı; 324 kayıt,
  2025-10-10–2026-10-09. Geçmişte yalnız price alanı var, ayrı ask yok.
  Canlı bid/ask belgelense de geçmiş price'ın satış olduğu doğrulanmadı;
  bu yüzden gram/TL geçmiş satışın yerine kullanılmayacak.

Erişilebilir alternatif bulundu, fakat bir yıllık gram/TL satış + güncel
alış gereksinimini tam karşılayan alternatif doğrulanamadı. Hesaplı Altın
destek yanıtı beklenirken altın SRC-02 açık tutulur. Döviz çalışmaları
altın beklenmeden ayrı kapsamda ilerleyebilir. Bu kontrolde DB/kod değişmedi.

## USD/EUR import (IMP-a) — 9 Kasım 2026

Kullanıcı kararları: K1-C (altın için önce resmî kaynak; günde tek değer yeter), tek `Price` korunur
(alış/satış ayrımı yok, migration yok), USD/EUR için **TCMB döviz alış** (`ForexBuying`), K3-C (son iki yıl),
K4-B (indirme ve içe aktarma ayrı komut), K5-A (eksik gün DB'ye yazılmaz, okuma tarafı en yakın önceki
günü alır), K6-A (farklı değer çelişkidir, üzerine yazılmaz), K7-A (altın ayrı PR).

Komutlar (repo kökünden):

```sh
dotnet run --project src/BankingAI.Cli -- market download tcmb --from 2024-11-09 --to 2026-11-09
dotnet run --project src/BankingAI.Cli -- market import tcmb --from 2024-11-09 --to 2026-11-09
```

- İndirme: yalnız hafta içi günler, sırayla (istekler arası 300 ms), 10 sn zaman aşımı, 5xx/zaman aşımında
  en fazla 3 deneme; 404 "yayın yok" sayılır ve tekrar denenmez. Gövde o günün bülteni olarak okunamıyorsa
  kaydedilmez. Arşiv `data/raw/market/tcmb/yyyy-MM-dd.xml` (git dışı); var olan gün tekrar istenmez.
- İçe aktarma: dosya başına USD ve EUR; birim 1, bülten tarihi dosyanın günü, değer yalnız rakam + nokta.
  Okunamayan gün "reddedildi", dosyası olmayan hafta içi gün "eksik" olarak raporlanır; ikisi de doldurulmaz.

Sonuç (9 Kasım 2026, 09:36):

| | Değer |
| --- | --- |
| İstenen hafta içi gün | 521 (2024-11-11 … 2026-11-09) |
| Yayın günü (arşivde) | 495; başarısız 0 |
| Yayın yok (404) | 26 |
| DB satırı | 990 (USDTRY 495 + EURTRY 495), Source `TCMB`, 2024-11-11 … 2026-11-06 |
| İkinci içe aktarma | eklenen 0, değişmeyen 990, çelişki 0 |

Yayın olmayan 26 günün 25'i resmî tatil ya da arife (elle karşılaştırıldı): 1 Ocak, Ramazan Bayramı
(31 Mart–1 Nisan 2025; 19–20 Mart 2026), 23 Nisan, 1 Mayıs, 19 Mayıs, Kurban Bayramı (5–9 Haziran 2025;
26–29 Mayıs 2026), 15 Temmuz, 28–29 Ekim. Biri (9 Kasım 2026) bugündür: bülten 15:30'da yayınlanır.
İşlemlerin ilk günü 1 Nisan 2025 bayramdır; en yakın önceki kur 28 Mart 2025 (USD 37,7656 / EUR 40,7019).

Bağımsız kontroller: eski indirmeyle (2025-10-10 … 2026-10-09) örtüşen 248 dosya bayt bayt aynı; DB'de
2025-10-10 USD 41,6388 / EUR 48,1872 ve 2026-10-09 USD 49,1274 / EUR 55,1112 (bu belgedeki tabloyla aynı),
hafta sonu satırı 0.

Sınırlar: güncelleme elle (zamanlayıcı yok); kur günü TCMB bülten günüdür (15:30 gösterge), banka işlem
fiyatı değildir; altın (GRAM_GOLD_TRY) henüz yok (SRC-02, IMP-04).
