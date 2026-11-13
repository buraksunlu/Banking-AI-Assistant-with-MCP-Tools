---
paths:
  - "src/BankingAI.Application/Analytics/**"
  - "src/BankingAI.Application/Investment/**"
  - "tests/BankingAI.Application.Tests/**"
  - "docs/analytics.md"
---

# Analytics (src/BankingAI.Application/Analytics)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.


- `PeriodResolver` + `TimeProvider`: sembolik dönem (`PeriodKind`) müşterinin `TimeZone`'una göre
  `[StartUtc, EndUtc)` aralığına çevrilir. ANA-20: gün/hafta dönemleri (hafta pazartesi; "son N gün" bugün dahil tam N gün,
  "son N ay" N ay önceki aynı günden; anlam tablosu docs/analytics.md). Testler ve demo "şimdi"yi
  `2026-10-10T12:00:00Z` olarak sabitler (`FixedTimeProvider`); demo verisi bu tarihe göredir.
- `CustomerScope` (internal) müşteri/saat dilimi, hesap sahipliği (başka müşterinin hesabı =
  `KeyNotFoundException`, varlık sızdırılmaz) ve dönem çözümünün tek yeridir. `PurchaseScope` onun
  üstüne yalnız Personal hesap + `Kind == Purchase` ekler. Çalıştırılmamış `IQueryable` dönerler;
  her servis tek bir `GROUP BY`/sayfa sorgusu ekler. Yetki kuralını servislerde kopyalama.
- Servisler: `SpendingSummaryService`, `CategoryBreakdownService`, `MerchantSpendingService`,
  `TransactionSearchService` (ekstre: tüm türler ve tüm hesaplar), `HighestTransactionsService`
  (varsayılan harcama; istenirse tüm işlemler; limit ≤ 100), `SpendingComparisonService`
  ("geçen aya göre" = son tam ay vs önceki ay; tek CASE WHEN sorgusu; aylık seri takvim ayı +
  devam eden ay, boş ay 0), `RecurringPaymentService` + saf `RecurringPaymentDetector` (son 6 ay;
  ≥3 ödeme, 26–35 gün aralık, ±%10 tutar, son ödeme ≤35 gün; tablo yok). İşlem listesi döndüren servisler
  `TransactionItemReader` (internal) ile aynı DTO'yu üretir: join'ler, maskeli karşı taraf
  (PairId), `LocalDate`, join'lerden sonra uygulanan sıralama. Join/maskeleme kodunu kopyalama.
  Sonuçlar para birimi başına satırdır; harcama yoksa boş liste döner (uydurma "0" satırı yok);
  yüzde backend'de 2 ondalık `AwayFromZero` ile hesaplanır ve 100'e zorlanmaz.
- `MerchantMatcher`: modelden gelen merchant metni güvenilmezdir. Kültürden bağımsız Türkçe
  sadeleştirme (sabit tablo; tr-TR'de `"I".ToLower()` = `"ı"`), tam → tek adaylı kısmi (≥3 harf) →
  Ambiguous → NotFound; yazım hatası düzeltilmez; adaylar yalnız müşterinin kendi merchant'ları.
  SQL'e ham metin değil eşleşen kanonik ad parametre olarak gider.
- ASP.NET tuzağı: `SuppressAsyncSuffixInActionNames = false` ayarlıdır; `CreatedAtAction(nameof(GetAsync), …)` buna dayanır
  (varsayılan ayarda action adı "Get" olur ve Location üretimi 500 verir).
- EF tuzakları: (1) constructor'la kurulan record'a projeksiyondan **sonra** `OrderBy` çevrilemez;
  `GroupBy(...).OrderBy(g => g.Key).Select(...)` kullan. (2) Nullable kolonda korelasyonlu join'de
  EF C# null semantiğini korur (`null == null` true) ve dış satırdaki null kontrolünü düşürebilir;
  kontrolü join'lenen satıra koy (`o.PairId != null && o.PairId == t.PairId`). (3) Npgsql 10
  `DateTimeOffset` saat dilimi dönüşümünü (`ConvertTimeBySystemTimeZoneId`) SQL'e çeviremez; yerel ay
  gruplaması için UTC pencere sınırlarını kullan.
- `Application/Investment/` (INV-01, todo 12f/§13): geçmiş fiyatla "ne olurdu" senaryoları. `MarketPriceReader` (tek sorgu, en yakın
  önceki gün, > 10 gün `Stale`, yok `Missing`; sonraki gün fiyatı asla), saf `WhatIfMath` (tek motor: TL / alış fiyatı → birim ×
  değerleme fiyatı; birim tam, yalnız rapor yuvarlanır; iki fiyat da `Ok` değilse rakam yok) ve `WhatIfService`. Harcama tutarı
  `CategoryBreakdownService`'ten, yalnız TRY (varlıklar TL fiyatlı, para birimi dönüştürülmez). Her sonuçta sabit
  `WhatIfMath.Disclaimer`; tahmin, ürün önerisi ya da emir yok; sonuç saklanmaz. Aylık birikim (`WhatIfMath.Monthly`,
  `MonthlyAsync`): son `months` tam ayın her 1'i (INV-03: zorunlu, 2–22 = `WhatIfMath.MinimumMonths`/`MaximumMonths`, varsayılan yok), aynı motor; dalgalanma aynı fiyat noktalarından (ayrı sorgu yok); tutar `amount`+TRY ya
  da `fromAdvice` (INC-02'yi backend çağırır, rakamı model taşımaz); `months` + 2 noktadan biri kullanılamazsa o varlıkta rakam yok.
