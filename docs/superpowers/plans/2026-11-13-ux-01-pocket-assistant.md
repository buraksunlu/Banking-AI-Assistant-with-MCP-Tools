# UX-01 Cep Asistanı — Uygulama Planı

> **Uygulayan için:** Bu plan görev görev uygulanır (önerilen: superpowers:executing-plans). Adımlar `- [ ]` kutularıyla izlenir.
> Proje kuralı aynen geçerli: her PR kendi dalında, önce plan → Burak "okay" → uygula → dur. Git komutlarını Burak çalıştırır.

**Amaç:** Sohbet uygulaması gibi uzun metin veren asistanı; kısa, görsel, günlük dille konuşan bir "cep asistanı"na çevirmek:
açık krem tema, "bu ay" paneli olan ana ekran, alttan kayan soru sayfası ve kodla kurulan kart cevaplar (Basit/Detaylı).

**Mimari:** Model yalnız araç seçer (K7-A); AI servisi araç sonucundan kartı **kodla** kurar (`ai/service/cards.py`) ve `/v1/chat`
cevabına isteğe bağlı `cards` listesi olarak ekler; .NET kapısı alanı olduğu gibi taşır; iPhone kartı şablonlarla çizer, sayı
biçimlemez ve hesaplamaz. Ana ekran paneli mevcut REST uç noktalarından gelir (model yok).

**Stack:** SwiftUI iOS 17+ (kütüphanesiz, Swift Charts), .NET 10 (Application + Api + McpServer), Python FastAPI (AI servisi).

**Tasarım belgesi:** `docs/superpowers/specs/2026-11-13-ux-01-pocket-assistant-design.md` (kararlar K1-A K2-B K3-A … K9-A).

## Genel kısıtlar

- Telefon para biçimlemez, yüzde/toplam hesaplamaz; yalnız gelen `share` ile çubuk boyu çizer.
- Para birimleri asla toplanmaz; birden çok para birimi = para birimi başına bir kart / bir satır.
- Ekranlarda sabit renk yok: her renk `DesignSystem/Palette.swift`'ten; aralıklar `Layout` ölçeğinden (kenar ve kart içi 16,
  kartlar arası 12, bölümler arası 24, köşe 20).
- Yazı/zemin ≥ 4,5:1; anlam taşıyan grafik ≥ 3:1; renkler gözle değil testle (UI-05 kuralı).
- Kod yorumları İngilizce (`///` özet + yalnız "neden"); commit ve PR'da AI imzası yok.
- Animasyon yalnız `Motion` üzerinden; "Hareketi azalt" açıkken hareket yok (UI-06).

## İnceleme odağı (testi hiçbir görevde kendiliğinden çıkmayan, en çok can yakabilecek beş durum)

1. **Açık temada okunmayan eski yazı:** bir ekran `Palette` yerine sistem rengine (ör. `.secondary`) yaslanıyorsa krem zeminde
   soluk kalır → PR1 Görev 5'teki ekran taraması her sekmeyi en büyük yazı boyutunda gezer.
2. **Sarı/turuncu yazı:** `accentFill` (#F5A700) yazı rengi olarak kullanılırsa beyazda 2,0:1 → PR1 Görev 3'te `accentFill`'i yazıda
   kullanan satır kalmadığını gösteren `grep` adımı.
3. **İki para birimi** (Elif EUR + TRY): panelde ve kartta toplam tek satır olmamalı → PR2 ve PR4'te Elif vakası.
4. **Kart kurulamayan araç:** `cards: []` gelir, eski metin cevabı görünür, hata çıkmaz → PR4 Python testi + PR5 iPhone testi.
5. **AI kapalıyken ana ekran:** panel yalnız REST'ten gelir; asistan bandı çıksa da panel dolu kalmalı → PR2 görünüm modeli testi.

---

## PR sırası (K9-A)

| PR | Dal | Kapsam | Backend | Model ölçümü |
|---|---|---|---|---|
| PR1 | `ux-01a-light-theme` | Z3 açık tema, bütün ekranlar | yok | yok |
| PR2 | `ux-01b-home-panel` | Ana ekran "bu ay" paneli (H2), hesaplar/tüm işlemler Profil'e | yok | yok |
| PR3 | `ux-01c-category-detail` | `get_category_spending` analitiği + API + MCP aracı (K4-A) | .NET | araç seçimi |
| PR4 | `ux-01d-card-contract` | `cards` sözleşmesi: `cards.py`, .NET, OpenAPI, ilk altı araç | Python + .NET | cevap doğruluğu |
| PR5 | `ux-01e-sheet-cards` | iPhone kayan sayfa (F1), kart şablonları, Basit/Detaylı, Asistan sekmesi aynı oturum (K2-B) | yok | yok |
| PR6 | `ux-01f-remaining-cards` | Kalan dokuz aracın kartları + ölçümler | Python | araç seçimi + cevap |

PR1 bu belgede ayrıntılıdır. PR2–PR6 her biri başlamadan önce kendi ayrıntılı planını (aynı biçimde: görevler, testler, elle
hesaplanmış değerler, karar soruları) alır; aşağıdaki özetler o planların sınırlarını sabitler.

---

## PR1 — Z3 açık tema (ayrıntılı)

### Burak'ın vereceği kararlar (PR1)

**Verildi (13 Kasım 2026): R1-A, R2-A.**

**R1 — Kategori renklerinin eşiği.** Kategori renkleri yalnız dolgu/çizgi olarak kullanılıyor (halka dilimi, çubuk, nokta:
`CategoryBreakdownSection`, `IncomeShareCard`, `IncomeShareDisplay`); adlar her zaman ana yazı renginde yanında yazılı. Bugünkü
test ise koyu temadan kalma bir kuralla gri dışındaki her kategoriden 4,5:1 (yazı eşiği) istiyor. Ölçtüm:

- **A) Grafik eşiği 3:1 + yumuşak set (önerim).** Beyaz kartta her kategori ≥ 3:1, kategoriler arası ΔE ≥ 25; anlamlı renklere
  (vurgu, gelen para, hata, gri) ΔE ≥ 20. Elle seçilmiş set (aşağıda) ölçüldü: kategoriler arası en yakın çift ≥ 25; anlamlı
  renklere en yakın üç çift turuncu–vurgu 23,7, nane–gelen para 20,0, arduvaz–gri 22,9. Artısı: renkler sıcak temaya uyuyor, WCAG'ın
  grafik kuralı (1.4.11) zaten 3:1. Eksisi: anlamlı renklere eşik 25 → 20 iner (turuncu kategori vurguya yakın; ama vurgu yazıda,
  kategori dolguda kullanılıyor, ikisi aynı yerde anlam taşımıyor).
- **B) Yazı eşiği 4,5:1'i koru.** 30 dk'lık arama yalnız çok cırtlak bir set buldu (pembe #E0009D, mor #7B00FF, mavi #0062EB…).
  Artısı: hiçbir kural gevşemez. Eksisi: krem temayla çatışıyor, Burak'ın beğendiği Z3 havası kaybolur.
- **C) 3:1 ama anlamlı renklere de 25.** Bulunamadı: turuncu kategori hem vurgudan hem hatadan 25 uzak, hem 3:1 olamıyor (sıcak
  temanın bedeli). Daha uzun arama gerekir; sonuç garanti değil.
- **D) Kategori sayısını düşür** (12 renk yerine 8 + gri, az kullanılanlar "diğer" tonu). Artısı: kolay ayrışır. Eksisi: kategori →
  renk eşlemesi değişir, Analiz ekranında iki kategori aynı renkte görünebilir; ayrı bir tasarım kararı.

Önerim **A**: renkler zaten yalnız grafik, adlar yazıyla yanında; B görünümü bozar, C/D zaman ve kapsam ister.

**R2 — Grafik çubuklarının rengi.** Mockup'taki sarı çubuk (#F5A700) beyazda 2,0:1; devam eden ayın soluk çubuğu (0,7) 1,65:1 —
bugünkü "soluk ay hâlâ 3:1 görünür" testi kalır mı?

- **A) Çubuk = koyu vurgu #9A5100 (önerim).** Tam 5,9:1, soluk (0,7) 3,23:1 → bugünkü test aynen geçer. Eksisi: mockup'tan biraz
  koyu (kahverengiye çalan turuncu).
- **B) Çubuk = #D97A00 (orta turuncu).** Tam 3,12:1, soluk 2,19:1 → soluk ay testi gevşer; ay "nokta" işaretiyle ayırt edilir.
- **C) Çubuk sarı #F5A700, ince koyu kenarlıkla** (#9A5100 1 pt). Mockup'a en yakın; kenarlık 3:1 sağlar. Eksisi: Swift Charts'ta
  kenarlıklı çubuk ek kod; küçük çubukta kenarlık dolguyu yer.
- **D) Sarı çubuk, kural yok.** Görünüm mockup'la aynı; açık zeminde çubuk zor seçilir, erişilebilirlik testi kaldırılır.

Önerim **A**: test ve erişilebilirlik korunur, değişiklik en küçük; sarı (`accentFill`) yalnız düğme dolgusunda kalır.

### Değerler (ölçüldü; `scratchpad` betiği, WCAG ve CIE76)

| Token | Eski | Yeni | Ölçüm |
|---|---|---|---|
| `background` | #0A0A0B | **#F6F1E7** (krem) | — |
| `surface` | #141416 | **#FFFFFF** | — |
| `raised` | #1C1C1F | **#EFE7D8** | — |
| `hairline` | #26262A | **#E2D8C4** | — |
| `primaryText` | #EDEDEF | **#2A2117** | 14,04 zemin · 15,81 kart · 12,87 raised |
| `secondaryText` | #8E8E93 | **#6E604D** | 5,42 · 6,10 · 4,96 |
| `accent` (yazı, bağlantı, seçili, çubuk) | #3D8BFF | **#9A5100** | 5,25 zemin · 5,91 kart · 4,81 raised |
| `accentFill` (yeni: düğme dolgusu) | — | **#F5A700** | yazı olarak kullanılmaz |
| `onAccent` (düğme yazısı) | #0A0A0B | **#2A1A00** | 8,35 `accentFill` üstünde |
| `incoming` | #5FCB8A | **#1E7F45** | 5,03 kart |
| `failure` | #FF6B6B | **#C8321F** | 5,34 kart · 4,74 zemin |

Tasarım belgesindeki bağlantı rengi `#c46a00` ölçümde krem zeminde 4,5'in altında kaldı → `#9A5100` (belge PR1'de düzeltilir).

Kategori seti (R1-A): orange #E0701A · lime #6E9A1F · brown #7A4E2E · pink #D6568C · purple #9B4FD0 · indigo #3E57C9 · teal #0B6F86 ·
cyan #2E9BD6 · magenta #B8389E · mint #2F9E7E · slate #8A6F86 · yellow #A88C00 · gray (sınıflandırılmamış) #8A7F70.
Beyazda en düşük: cyan 3,10, yellow 3,27, orange 3,23; gri 3,92.

İskelet dolgusu: `Skeleton.fillOpacity` 0,16 → **0,18** (0,16'da krem zeminde ΔE 11,7 < 12; 0,18'de ≥ 12, kontrast ~1,4 < 3).

### Dosyalar

- Değişir: `ios/BankingAI/BankingAI/DesignSystem/Palette.swift` (değerler, `accentFill`, kategori tonları),
  `DesignSystem/Skeleton.swift` (`fillOpacity`), `DesignSystem/Components.swift:33` (birincil düğme dolgusu),
  `Chat/ChatView.swift:192` (gönder düğmesi dolgusu), `App/BankingAIApp.swift:20` (`.light`),
  `Assets.xcassets/AccentColor.colorset`, `Assets.xcassets/LaunchBackground.colorset` (açık ve koyu varyant aynı değer).
- Testler: `BankingAITests/DesignSystemTests.swift`, `CategoryPaletteTests.swift`, `IdentityTests.swift`, `LoadingStateTests.swift`.
- Belge: `docs/ios.md` (yeni bölüm "iOS-t açık tema"), tasarım belgesi bağlantı rengi, `RULE.md` §29, `todo.md` UX-01 PR1.

### Görev 1: Palet değerleri ve yazı kontrastı

**Arayüzler:** Üretir `Palette.accentFill: Swatch` (yeni). Diğer token adları aynı kalır (ekranlar değişmeden yeni renge geçer).

- [ ] **Adım 1: Testleri yeni değerlere çevir (kırmızı).** `DesignSystemTests`:
  - `everyTextColourIsReadableOnItsSurface` listesine `("accent on raised", Palette.accent, Palette.raised)` ekle;
    `("button label on accent", Palette.onAccent, Palette.accent)` → `("button label on the button fill", Palette.onAccent, Palette.accentFill)`.
  - `theBackgroundIsNearBlackNotPureBlack` → `theBackgroundIsTheWarmCreamNotPureWhite`:
    `#expect(Palette.background == Swatch(0xF6F1E7))`, `#expect(Palette.surface == Swatch(0xFFFFFF))`, iki `!=` satırı aynen.
  - Yeni `theButtonFillIsNeverReadAsText`: `#expect(Palette.accentFill.contrast(with: Palette.surface) < 3)` — sarı yazı olarak
    seçilirse okunmaz; bu test değeri bilerek sabitler ve Görev 3'teki `grep` adımıyla birlikte anlam taşır.
- [ ] **Adım 2: Çalıştır, kırmızıyı doğrula.** Komut `.claude/rules/ios.md`'deki `xcodebuild test` (yalnız `-only-testing:BankingAITests/DesignSystemTests`).
  Beklenen: `background` eşitliği ve `accentFill` yok derleme hatası.
- [ ] **Adım 3: `Palette.swift` değerlerini tablodaki gibi yaz**, `accentFill`'i `/// Button fill only; never text.` özetiyle ekle;
  dosya başındaki "always dark" açıklamasını "always light (UX-01 K6-A)" yap.
- [ ] **Adım 4: Testi çalıştır, yeşil.**

### Görev 2: Açık tema, simge rengi ve açılış rengi

- [ ] **Adım 1: `IdentityTests` güncelle (kırmızı).** `theAccentIsTheIconsBlueAndNotTheIncomingGreen` →
  `theAccentIsTheDarkAmberAndNotTheIncomingGreen`: `#expect(Palette.accent == Swatch(0x9A5100))`, `accent != incoming` satırı aynen.
  `theLaunchColourIsThePaletteBackground` ve `theAssetCatalogueAccentIsTheSameColourAsThePalette` değişmez (paletle kıyaslıyorlar;
  asset eski değerde olduğu için kırmızı olurlar).
- [ ] **Adım 2: Kırmızıyı doğrula** (asset karşılaştırmaları ve vurgu eşitliği kırmızı).
- [ ] **Adım 3:** `AccentColor.colorset` ve `LaunchBackground.colorset` içindeki açık ve koyu varyantı yeni değerlere yaz
  (#9A5100, #F6F1E7; sRGB, 0–1 ondalık). `BankingAIApp.swift:20` `.preferredColorScheme(.dark)` → `.light`.
- [ ] **Adım 4: Yeşil.** Simülatörde açılış ekranı krem → ilk ekran krem (renk sıçraması yok).

### Görev 3: Düğme dolgusu ile yazı vurgusunu ayır

- [ ] **Adım 1:** `Components.swift:33` (`PrimaryButtonStyle` arka planı) ve `ChatView.swift:192` (gönder düğmesi) `Palette.accent` →
  `Palette.accentFill`. Etiketler zaten `onAccent`. `LoginView.swift:58` ilerleme göstergesi `onAccent` kalır.
- [ ] **Adım 2: Doğrula:** `grep -rn "accentFill" ios/BankingAI/BankingAI` yalnız bu iki satırı ve `Palette.swift`'i göstermeli
  (yazıda kullanım yok). Diğer `Palette.accent` kullanımları (bağlantı, seçili çip, çerçeve, çubuk R2-A, isteğe bağlı dilim) aynen kalır.
- [ ] **Adım 3:** Tam test paketi yeşil.

### Görev 4: Kategori renkleri ve iskelet

- [ ] **Adım 1: `CategoryPaletteTests` güncelle (kırmızı)** (R1-A'ya göre):
  - `everyCategoryColourIsReadableOnTheCard` → `everyCategoryColourIsSeenAsAGraphicOnTheCard`: her ton `>= 3` (4,5 satırı kalkar;
    neden yorumu: "category colours are fills beside a name in primary text; WCAG 1.4.11 asks 3:1 of a graphic").
  - `noTwoCategoriesLookAlike` aynen (≥ 25).
  - `noCategoryLooksLikeTheAccentMoneyInOrAnError`: eşik `>= 20`, karşılaştırılanlara `Palette.accentFill` eklenir.
  - Yeni: gri dışındaki her ton gri tona `>= 20` (sınıflandırılmamış dilim kategoriyle karışmasın).
  - `theUnclassifiedGroupKeepsItsGrey` → `#expect(CategoryTone.gray.swatch == Swatch(0x8A7F70))`.
  - `theMonthStillRunningIsPaleButStillAGraphic` aynen (R2-A ile 3,23 ≥ 3).
- [ ] **Adım 2: Kırmızıyı doğrula.**
- [ ] **Adım 3:** `CategoryTone` değerlerini setteki gibi yaz; `Skeleton.fillOpacity = 0.18`.
- [ ] **Adım 4:** `LoadingStateTests.theBlockGreyIsSeenButDoesNotLookLikeContent` dahil tam paket yeşil.

### Görev 5: Ekran taraması, kasıtlı bozma, belge

- [ ] **Adım 1: Simülatör taraması (Murat ve Elif):** Giriş, Ana sayfa, Analiz (halka + aylık çubuk), Asistan (balon, öneri çipi,
  seçenek kartı, hata bandı), Profil + Çıkış onayı, İşlem detayı. Her ekranda varsayılan ve en büyük yazı boyutu; okunmayan yazı,
  kaybolan çerçeve/ayırıcı, koyu temadan kalma gölge var mı. Bulunan her düzeltme ayrı satırla PR açıklamasına.
- [ ] **Adım 2: Kasıtlı bozma (hepsi bir testle yakalanmalı, sonra geri alınır):** `secondaryText` #9A8C78 (4,5 altı) · `accent` #C46A00
  · `onAccent` #FFFFFF · `background` #FFFFFF · `LaunchBackground` asset'i #FFFFFF · `teal` = `cyan` · `gray` #6E6E73 · `fillOpacity` 0,10.
- [ ] **Adım 3: Belge:** `docs/ios.md` "iOS-t": tablo + iki karar (R1, R2) + ölçüm yöntemi; tasarım belgesinde `#c46a00` → `#9A5100`;
  `RULE.md` §29 ("her zaman açık tema", UI-01/UI-04 "koyu/mavi" kararlarının yerine); `todo.md` UX-01 PR1 `[x]` + test sayısı.
- [ ] **Adım 4: Git (Burak):** dal `ux-01a-light-theme`, mesaj `move the iphone app to the warm light theme (UX-01 PR1)`.

**Beklenen sonuç:** iOS test sayısı aynı (yeniden adlandırma) + 2 yeni test; ViewModel/ağ kodu değişmez.

---

## PR2 — Ana ekran "bu ay" paneli (özet)

- **Ne:** `HomeView` gövdesi üç kart: (1) "Bu ay harcama · N gün" `period-comparison?comparison=MonthToDate` (büyük rakam, aynı
  günlerle fark ve yön), (2) "Nereye gitti" `category-breakdown?period=ThisMonth` (ilk 3–4 kategori, `share` çubuğu, "Tümü ›" Analiz'e),
  (3) "Gelir payı · <geçen ay>" `income-share?period=LastMonth` + "Nerede kısarım? ›". Hesaplar ve son işlemler Profil'e
  ("Hesaplarım", "Tüm işlemler" satırları); mevcut `AccountsSection`/`RecentTransactionsSection` taşınır, yeniden yazılmaz.
- **Satır dokunuşu (K5-A):** PR2'de satırlar dokunulabilir değil (kayan sayfa PR5'te); PR5 bağlar. PR2'de yer tutucu yok.
- **Para birimi:** her kart para birimi başına satır (Elif).
- **Elle değerler (Murat, demo anı 10 Ekim 2026):** bu ay 8.241,40 TL, aynı günler 8.876,40 TL (%7 az); Market 5.133,00 ·
  Akaryakıt 2.658,50 · Fatura 449,90; Eylül gelir payı %63 (53.388,39 / 85.000).
- **Testler:** panel görünüm modeli (üç istek bağımsız; biri hata verirse diğerleri dolu; AI kapalıyken dolu), yükleniyor/boş/hata
  UI-06 bileşenleriyle; kasıtlı bozma.
- **Karar soruları planında:** kart sırası, "Nereye gitti"de kaç kategori, gelir payı kartı yokken (maaşsız persona) ne görünür.

## PR3 — Kategori detayı backend'i (K4-A, özet)

- **Ne:** `get_category_spending(period, categoryCode)` — Application servisi + `GET /api/analytics/category-spending` + MCP aracı.
  Sonuç: dönem toplamı, önceki dönem toplamı ve yüzde fark, işyeri dağılımı (tutar + pay), işlem sayısı, işlem başı ortalama, en büyük
  işlem (tutar, işyeri, gün), son 6 tam ayın serisi ve ortalaması, toplam harcamadaki pay. Para birimi başına.
- **Elle değerler (Murat, Market, Eylül 2026):** 18.016,50; Ağustos 11.944,50; +%50,84; BİM 6.216,00 · Migros Jet 5.317,50 ·
  Migros Sanal Market 3.577,50 · A101 2.905,50; Nis–Eyl 8.654,00 / 10.339,00 / 12.896,00 / 16.078,50 / 11.944,50 / 18.016,50,
  ortalama 12.988,08, ortalamanın %38,7 üstü; 8 işlem, ortalama 2.252,06; en büyük 3.476,00 BİM 3 Eylül; 53.388,39 içinde %33,7.
  Değerler SQL ile bağımsız doğrulanır.
- **Testler:** Application (rakamlar, boş dönem, kategorisiz, iki para birimi, sorgu sayısı), Api (`EndpointSweepTests`, OpenAPI kopyası),
  McpServer (argüman filtresi, enum), Python araç seçimi vakaları (eski vakalarda bozulan yok).
- **Karar soruları planında:** "son 6 ay" devam eden ayı içerir mi, dönem `Custom` kabul edilir mi, en büyük işlemde eşitlik.

## PR4 — Kart sözleşmesi ve ilk altı araç (özet)

- **Ne:** `ai/service/cards.py` (araç başına kurucu, `outcome_for` kalıbı), `/v1/chat` cevabına `cards: list[Card]` (boş liste =
  metin cevabı), .NET `AssistantReply.Cards` taşıma + sözleşme testi + OpenAPI kopyası. İlk altı araç (K3-A): özet, kategori dağılımı,
  kategori detayı (PR3), işyeri harcaması, karşılaştırma, aylık seri. Blok türleri `bars`, `series`, `stats`; `rows`/`assets` PR6'da.
- **Kurallar:** her blokta sayı + hazır `text`; `share`/`percent` Python'da backend sonucundan; sayı denetimi kartın bütün `text`
  alanlarını da tarar; kalıp cümle (K1-A) koddan, rakam yalnız sonuçtan; kart varken model açıklama yazmaz (K7-A).
- **Testler:** her kurucu için Murat değerleri (PR3 tablosu), iki para birimi = iki kart, bilinmeyen araç = `[]`, denetim kartta
  uydurulmuş rakamı yakalar; `answer_eval` kart alanlarını da kontrol eder; kasıtlı bozma.

## PR5 — iPhone kayan sayfa, kart şablonları, cevap modu (özet)

- **Ne:** `MainTabView`'da tek `ChatViewModel` (Asistan sekmesi ve kayan sayfa aynı oturum, K2-B); ana ekrandaki "Kılavuz'a sor" ve
  panel satırları kayan sayfayı açar (K5-A, satır koddan kurulmuş soruyu gönderir); kart şablonları `BarsBlock`, `SeriesBlock`,
  `StatsBlock` (adlar planında kesinleşir); kart köşesinde Basit/Detaylı anahtarı; Profil'de varsayılan (`UserDefaults`, backend'e
  gitmez). `cards` boşsa bugünkü cevap görünümü.
- **Testler:** şablon görünüm modelleri yalnız `text`/`share` kullanır (hesap yok), mod anahtarı istek atmaz, sekme değişince oturum
  sürer, çıkışta silinir; simülatörde Murat senaryosu.

## PR6 — Kalan dokuz aracın kartları ve ölçümler (özet)

- **Ne:** `rows` (düzenli/yaklaşan ödemeler, en büyük işlemler, arama), `assets` (iki "ne olurdu"), bütçe, tasarruf, gelir payı, öneri.
- **Ölçümler:** araç seçimi, cevap doğruluğu (kart alanları), ifade testi; eşik: eski vakalarda bozulan yok. Model işi aralıklı,
  sonunda `ollama stop qwen3.5:4b`.
