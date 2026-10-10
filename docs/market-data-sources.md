# Günlük TL fiyatları — kaynak incelemesi

İnceleme tarihi: 10 Ekim 2026. Bu belge kaynak araştırmasıdır; import henüz uygulanmadı.

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
ForexBuying ve ForexSelling farklı serilerdir; import öncesi tek fiyat tanımı
SRC-03'te seçilecek. Bunlar gerçek müşteri işlem fiyatı olarak sunulmayacak.
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
