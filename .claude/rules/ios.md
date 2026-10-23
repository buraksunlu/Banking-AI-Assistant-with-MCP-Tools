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
Testlerde ağ `MockURLProtocol` ile, test başına ayrı uydurma sunucu adıyla (paralel güvenli). Sohbet (AI-08): `ChatViewModel` ana
ekranın `@State`'inde yaşar (geçmiş yalnız bellekte, çıkışta silinir); yalnız sohbet isteği 40 sn bekler (30 → 35 → 40); 503 bant + kapalı
giriş, diğer hatalar balonda "Tekrar dene" (aynı balon); Durdur ve ekrandan çıkış isteği iptal eder. Öneriler (CHAT-06): `suggestions` son cevabın altında, gönderince boşalır; boş sohbette
`open()` başlangıç sorularını ister (hata/boş → `ChatViewModel.examples`). Hesaplar (IOS-04) ana ekranın gövdesidir (`AccountsViewModel`);
tutar her ekranda `Formatting/Money` ile yazılır (sohbetle aynı: `577.490,18 TL`), mobilde hesap yapılmaz. "Tüm işlemler" `APIRequest.history` ile bütün geçmişi
imleçle gezer (API-17; son işlemler kartı dönemsiz, son 3 ay). İşlem satırının metni yalnız
`Transactions/TransactionDisplay`'de (gün `localDate`'ten, işaret `direction`'dan); sorgu parametreleri `APIRequest.query` ile (yola "?" yazılmaz). Demo şifresini Claude yazmaz/okumaz:
simülatörde girişi kullanıcı yapar.
Grafikler (IOS-08) `Analytics/` altındadır: rakam, toplam ve yüzde backend'den gelir, telefon yalnız çizer ve yazar; kategori rengi `AnalyticsDisplay.tone`'da sabit (kategorisiz hep gri), dönemler `AnalyticsPeriod` adlarıyla gönderilir (telefon tarih hesaplamaz), para birimleri ayrı grafiktir.
