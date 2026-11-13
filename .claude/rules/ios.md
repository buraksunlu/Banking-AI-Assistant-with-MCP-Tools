---
paths:
  - "ios/**"
  - "docs/ios.md"
---

# iPhone uygulaması (ios/)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

iPhone uygulaması (`ios/BankingAI`, docs/ios.md; Xcode 26.5, iOS 17+, Swift Testing). Simülatör `http://localhost:5248`'e ulaşır:

```sh
xcodebuild -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI -destination 'platform=iOS Simulator,name=iPhone 17 Pro,OS=26.5' test
```

iOS kuralları: `View → @Observable ViewModel → APIClient → URLSession`; View'da ağ kodu yok, kütüphane yok. Proje "eşitlenen klasör"
kullanır: `ios/BankingAI/BankingAI/` altına eklenen dosya projeye kendiliğinden girer, `project.pbxproj` elle düzenlenmez. Kod varsayılan
olarak `MainActor` (Xcode 26 ayarı). Token yalnız Keychain'de; token'lı çağrıda 401 → oturum kapanır (giriş çağrısında değil).
Testlerde ağ `MockURLProtocol` ile, test başına ayrı uydurma sunucu adıyla (paralel güvenli). Gezinme (UI-07): giriş sonrası `App/MainTabView` (sistem `TabView`, sekmeler `AppTab`: Ana sayfa · Analiz · Asistan · Profil, her
sekme kendi `NavigationStack`'inde); bütün ekran modelleri orada yaşar, saat dilimi ortamı (`customerTimeZone`) orada bütün sekmelere
verilir; bir ekran tek yerde açılır (Ana sayfadaki kart ve daire sekmeye geçirir, aynı ekranı push etmez). Profil `HomeViewModel.profile`'ı
okur (ikinci `/api/me` yok), Çıkış onaylıdır. Sohbet (AI-08): `ChatViewModel` `MainTabView`'ın
`@State`'inde yaşar (geçmiş yalnız bellekte, çıkışta silinir); yalnız sohbet isteği 40 sn bekler (30 → 35 → 40); 503 bant + kapalı
giriş, diğer hatalar balonda "Tekrar dene" (aynı balon); Durdur ve uygulamanın arka plana geçmesi isteği iptal eder; sekme değişimi etmez (UI-07 K5-B). Öneriler (CHAT-06): `suggestions` son cevabın altında, gönderince boşalır; boş sohbette
`open()` başlangıç sorularını ister (hata/boş → `ChatViewModel.examples`). Seçenekler (CHAT-10 PR3): geri sorunun `choices`'u son cevabın altında tek
kart (`Chat/ChoiceList`), dokununca yalnız etiket gider (ekranda Türkçe baş harf, gönderilen değişmez); kartın SwiftUI kimliği soruya bağlı
(sabit kimlik eski satırları tutar); tutar sorusunda son satır `inputHint`, dokununca yazı alanına odaklanır (mesaj göndermez); gönderimde ve hatada boşalır, seçenekli cevapta öneri gösterilmez; `choices` alanı zorunlu
ve bozuk seçenek bütün cevabı düşürür (link gibi atlanmaz). Hesaplar (IOS-04) ana ekranın gövdesidir (`AccountsViewModel`);
kartta numara `AccountDisplay.shortNumber` ile kısalır ("•••• 001", UI-03), tam numara hesap detayında; ana ekranın "Bu ay harcama" kartı ve üst satırdaki gün `SpendingSummaryViewModel`den gelir (telefon tarih okumaz); tutar her ekranda `Formatting/Money` ile yazılır (sohbetle aynı: `577.490,18 TL`), mobilde hesap yapılmaz. "Tüm işlemler" `APIRequest.history` ile bütün geçmişi
imleçle gezer (API-17; son işlemler kartı dönemsiz, son 3 ay). İşlem satırının metni yalnız
`Transactions/TransactionDisplay`'de (gün `localDate`'ten, işaret `direction`'dan); sorgu parametreleri `APIRequest.query` ile (yola "?" yazılmaz). Demo şifresini Claude yazmaz/okumaz:
simülatörde girişi kullanıcı yapar.
Grafikler (IOS-08) `Analytics/` altındadır: rakam, toplam ve yüzde backend'den gelir, telefon yalnız çizer ve yazar; kategori kodu→ton `AnalyticsDisplay.tone`'da sabit (kategorisiz hep gri), ton→renk `Palette.swift`'te `CategoryTone.swatch` (UI-05, UX-01 R1-A; kontrast ve Lab ΔE farkı `CategoryPaletteTests` korur, kategori rengi vurgu/gelen para/hata rengine ve griye benzemez), dönemler `AnalyticsPeriod` adlarıyla gönderilir (telefon tarih hesaplamaz), para birimleri ayrı grafiktir. Aylık grafikte (`MonthlyTrendViewModel`) devam eden ay (`isPartial`) tam ayla karıştırılmaz, harcamasız ay atlanmaz, tutar eksende yuvarlanıp yazılmaz.
Durumlar (UI-06): yükleniyor yalnız `.skeleton()` ile (gerçek satır + `Skeleton`'daki nötr sahte veri; satır sayısı gerçek listeyle aynı, `LoadingStateTests`), boş/hata yalnız `StateMessage` ile (`ContentUnavailableView` ve liste çarkı kullanılmaz). Animasyon yalnız `Motion` üzerinden (`.motion(value:)` ya da `Motion.animation(_:reduceMotion:)`); "Hareketi azalt" açıkken hiç animasyon yok. Titreşim yalnız gönderim ve yanlış girişte. AX boyutlarında yan yana düzen kesiliyorsa dikeye geçer (`dynamicTypeSize.isAccessibilitySize`), dokunma alanı en az 44 pt.
Adlar (UI-04): uygulama "Compass", asistan "Kılavuz"; ekran metinlerinde ad yazılmaz, `DesignSystem/Brand`'den okunur. Görünen ad Info.plist'te de durur (`IdentityTests` eşitliği denetler); simge `AppIcon.appiconset/icon.png` (kullanıcının görselinden kırpılmış, tek yuva). Tema her zaman açık (UX-01 K6-A, Z3; UI-01'in siyahı ve UI-04'ün mavisi yerine): krem zemin `#F6F1E7`, beyaz kart; vurgu koyu kehribar `#9A5100` (yazı, bağlantı, seçili, grafik çubuğu), sarı `accentFill` `#F5A700` yalnız düğme dolgusu, asla yazı; yeşil yalnız gelen para, vurgu asla yeşil olmaz. Kategori renkleri grafiktir: kartta ≥ 3:1, kendi aralarında ΔE ≥ 25, anlamlı renklere ve griye ΔE ≥ 20 (R1-A).
Gelir payı kartı (INC-04) `Insights/` altındadır: tek istek `spending-advice?period=LastMonth` (ay backend'in), paylar ve öneri metni backend'den, telefon yalnız çizer; çubuk %100'de kırpılır (metin tam), en fazla 2 öneri, bilinmeyen grup adı çözümleme hatasıdır (başka grup gibi çizilmez); kart modele bağlı değildir. İleride IOS-12 içgörü bölümüne taşınır.
