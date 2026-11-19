# Rutin / rutin olmayan harcama dökümü ve yatırım karşılaştırması (DEMO-02)

Tarih: 19 Kasım 2026. Durum: tasarım, kullanıcı onayı bekliyor.

## Neden

Kullanıcı "Geçen ay ne kadar harcadım?" ya da "5 Eylül'de ne harcadım?" diye sorup "İşlemleri gör"e dokunduğunda harcamalarının hangi
kısmının kaçınılmaz, hangi kısmının kendi tercihi olduğunu tek bakışta görmeli; tercih olan kısım için "bu para yatırılsaydı ne olurdu"
bilgisi ve alışkanlık önerisi almalı. Demo kişisi Murat'ın verisi (DEMO-01) bunu gösterecek biçimde hazır.

## Kullanıcı kararları (19 Kasım 2026)

- İki grup: **Rutin ve temel** (rutin ödemeler + temel kategoriler) / **Rutin olmayan** (geri kalan her şey). Rutin olmayanların hepsi öneri alır;
  kategorisi belirsiz olanlar (Diğer / sınıflandırılmamış) grupta görünür ama öneri ve yatırım hesabına girmez.
- **K1 (güncellendi):** satır satır not yok. Rutin olmayanların **toplamı** için dolar, euro ve gram altın, üçü birlikte. İşlem detayında tek
  işlem için aynı üç varlık.
- **K2-A:** rutin = dönemin son gününe göre düzenli ödeme kuralı (son 6 ay) **ya da** açıklamada `TAKSIT nn/NN` (ilk taksit de rutin).
- **K3-A:** alışkanlık önerisi: isteğe bağlı bir işyerinde dönemin harcaması son 6 tam ayın aylık ortalamasının ≥ 1,5 katı ve fark ≥ 500 TL ise;
  en fazla 2, büyük fark önce; metin sabit şablon, kod yazar.
- **K4-A:** alışkanlık önerisi yalnız ≥ 28 günlük dönemde; kısa dönemde gruplar ve yatırım karşılaştırması var, öneri yok.
- **K5-A:** 7 günden yeni harcama yatırım hesabına girmez; hepsi yeniyse "karşılaştırmak için erken" yazılır.
- **K6-A:** yer: sohbetteki "İşlemleri gör" (yalnız harcama bağlantıları, dönem ≤ 31 gün) ve işlem detayı. Diğer listeler düz kalır.

## Hedefler / hedef olmayanlar

- Hedef: backend hesaplar ve sınıflandırır, telefon yalnız gösterir; model hiçbir rakam yazmaz (sohbet akışı değişmez).
- Hedef olmayan: yeni tablo/migration, sohbet kartına yeni blok, MCP aracı, gelecek tahmini ya da ürün önerisi, Tüm işlemler / hesap ekranı.
- Kaçınılacak: telefonda toplama/yüzde; iki para birimini toplamak; rutin bir ödemeye "yatırsaydın" demek; sabit uyarısız yatırım rakamı.

## Yaklaşımlar (seçilen: A)

- **A. Yeni "harcama dökümü" ucu (seçildi):** `GET /api/transactions/breakdown` bağlantıyla aynı filtreleri alır, sayfasız gruplu cevap döner;
  tek işlem için `GET /api/transactions/{id}/insight`. Mevcut uçlar ve sözleşmeleri değişmez.
- B. Sayfalı listeye alan eklemek: grup toplamları sayfalara bölünür, her sayfada fiyat sorgusu, mevcut sözleşme değişir.
- C. Yalnız sohbet kartı: kullanıcı liste ve detay istedi.

## Tasarım

### Backend (PR1)

`Application/Analytics/SpendingBreakdownService` (yeni, Scoped, somut sınıf), `CustomerScope` + `PurchaseScope` üzerinden (yalnız kişisel hesap,
`Kind == Purchase`; yetki kuralı kopyalanmaz).

**`GET /api/transactions/breakdown`** — parametreler: `period` (zorunlu, `AllTime` yok), `from`/`to` (Custom), `categoryCode`, `merchant`.
Dönem 31 günü aşarsa 400 `period_too_long` (telefon düz listeye döner). Cevap para birimi başına:

```text
{ period, currencies: [ {
    currencyCode,
    routine:    { total, count, items: [ { transaction, reason: Subscription|Bill|Installment|Recurring|Essential, installment: "03/12"? } ] },
    nonRoutine: { total, count, items: [ { transaction, eligible: bool } ],
                  investment: { amount, excludedRecent, method: "EachOnItsOwnDay", valueDate, assets: [3 × AssetWhatIf], status, disclaimer } | null,
                  advice: [ { merchant, count, amount, usualMonthly, excess, message } ] } } ] }
```

- **Sınıflandırma:** `RoutineClassifier` (saf): taksit kalıbı `TAKSIT (\d{2})/(\d{2})` → Installment; değilse düzenli ödeme kuralı (dönemin son
  günü "bugün", pencere `Last6Months`, grup hesap + para birimi + işyeri) → kategori SUBSCRIPTION ise Subscription, BILLS ise Bill, değilse
  Recurring; değilse `SpendingGroups.Of(kategori) == Essential` → Essential; değilse rutin olmayan. Rutin olmayan satırda
  `eligible = grup Discretionary` (Unknown öneri almaz).
- **Yatırım karşılaştırması (yalnız TRY):** uygun ve ≥ 7 günlük satırların her biri **kendi gününde** alınmış sayılır, bugünkü fiyatla değerlenir
  (yeni saf `WhatIfMath.Basket`: birimler toplanır, yalnız rapor yuvarlanır); fiyatlar tek sorguda (`MarketPriceReader`), bir gün eski/eksikse o
  varlıkta rakam yok (mevcut kural). `excludedRecent` = dışarıda kalan yeni tutar. Uygun satır yoksa `investment = null`.
- **Alışkanlık önerisi:** yalnız dönem ≥ 28 gün; uygun satırlar işyerine göre; ortalama = dönem başından önceki 6 tam ayın toplamı / 6 (aynı
  işyeri, harcama kapsamı); kural K3-A; şablon: "{İşyeri} için bu dönemde {n} kez {tutar} harcadınız; son 6 ayın aylık ortalaması {ortalama}.
  Ortalamanızda kalsaydınız {fark} size kalırdı."
- **`GET /api/transactions/{id}/insight`** — tek işlem: `{ transaction, routine: {reason, installment}?, group: Essential|Discretionary|Unknown,
  investment: { assets[3], status, disclaimer } | null, note }`. Yatırım yalnız rutin olmayan + Discretionary + TRY + ≥ 7 gün; değilse `note`
  sebebi söyler ("Rutin ödeme", "Temel harcama", "Karşılaştırmak için erken", "Yalnız TL harcamalarda"). Başka müşterinin işlemi = 404.
- Hatalar, JSON, OpenAPI kopyası, uç taraması (`EndpointSweepTests`) ve izolasyon seti (`IsolationTests`) mevcut kurallarla güncellenir.

### iPhone (PR2)

- `LinkedTransactionsView`: bağlantı harcama bağlantısıysa önce döküm istenir; iki bölüm başlığı (ad + toplam + adet), satırlar mevcut
  `TransactionRow` (rutin satırda küçük etiket: Abonelik / Fatura / Taksit 3/12 / Temel), "Rutin olmayan"ın altında yatırım kartı (üç varlık:
  bugünkü değer + fark, fiyat günü, sabit uyarı; "dahil edilmeyen son 7 gün" satırı) ve en fazla 2 öneri satırı. `period_too_long` → bugünkü
  düz liste. Telefon toplama yapmaz.
- `TransactionDetailView`: "Bu harcama yatırılsaydı" bölümü (üç varlık) ya da sebebi söyleyen tek satır; rutin ise etiket.

### Doğrulama

- Application testleri sabit demo veritabanında, elle hesaplanmış değerlerle (dönem, gruplar, taksit, öneri eşikleri, 7 gün kuralı,
  `Basket` hesabı); saf sınıflar DB'siz. Api testleri: uçlar, 400 `period_too_long`, 404 izolasyon, OpenAPI. Kasıtlı bozma.
- Canlıda Murat ile: "geçen ay" (Ekim 2026) ve "5 Eylül 2026" dökümleri SQL ile karşılaştırılır; iPhone simülatörde elle bakılır.

## Yapımcıya bırakılanlar (benim kararlarım; kullanıcı düzeltebilir)

- Yatırım hesabında her harcama **kendi gününde** alınmış sayılır ("harcamasaydım" aracı dönem başında tek alım yapar; burada para o gün
  harcandığı için kendi günü daha dürüst). Sohbetteki "harcamasaydım" rakamından farklı çıkabilir; kartta yöntem bir satırla yazılır.
- Uç adı `insight`, alan adları ve etiket metinleri.
- Döküm sayfasız; ≤ 31 gün sınırı en kalabalık personada bir ay ≈ 100 satır demek.
