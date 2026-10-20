# iOS uygulaması (SwiftUI)

`ios/BankingAI/` (Xcode 26.5, Swift 6.3, en düşük iOS 17). Kurallar RULE.md: yalnız Swift + SwiftUI, MVVM, URLSession, Codable,
NavigationStack, üçüncü parti kütüphane yok. Uygulama yalnız .NET API'ye konuşur; finansal hesap mobilde yapılmaz.

## iOS-a: proje temeli ve giriş (IOS-01, IOS-03 çekirdeği, IOS-13; 20 Ekim 2026)

```text
ios/BankingAI/
  BankingAI.xcodeproj          (eşitlenen klasörler: klasöre eklenen dosya projeye kendiliğinden girer)
  BankingAI/
    App/         BankingAIApp (oturum + istemci bir kez kurulur), RootView (giriş mi ana ekran mı), AppConfig (API adresi)
    Networking/  APIClient (tek ağ kapısı), APIError (+ Türkçe mesaj), APIModels (Codable)
    Session/     SessionStorage (Keychain), SessionStore (@Observable oturum)
    Login/       LoginView, LoginViewModel
    Home/        HomeView, HomeViewModel (geçici ana ekran: "Merhaba, Ada!" + Çıkış; sohbet AI-08'de)
    Info.plist   yalnız yerel ağa düz HTTP (NSAllowsLocalNetworking)
  BankingAITests/  Swift Testing; sahte ağ (MockURLProtocol, test başına ayrı sunucu adı), bellekte oturum
```

Akış: `View → ViewModel (@Observable) → APIClient → URLSession → .NET API`. View'larda ağ kodu yok. Xcode 26 yeni projelerde
bütün kodu varsayılan olarak `MainActor`'a bağlar (`SWIFT_DEFAULT_ACTOR_ISOLATION = MainActor`); ağ beklemeleri `await` ile
arayüzü dondurmaz.

### Kurallar

- **Token yalnız Keychain'de** (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`; yedeğe ve başka cihaza gitmez), UserDefaults'ta
  değil. Şifre hiçbir yerde saklanmaz; her denemeden sonra alandan silinir.
- **401:** token'lı bir çağrıda 401 → oturum kapanır, giriş ekranı gelir (`APIClient.onUnauthorized`). Giriş çağrısındaki 401
  yalnız "Kullanıcı adı veya şifre hatalı." demektir; oturuma dokunmaz.
- **Süre:** token'ın bitişi (`expiresAt`, 60 dk) saklanır; uygulama açılırken ya da çağrı öncesi süresi geçmişse oturum kapanır.
  Token yoksa çağrı ağa hiç çıkmaz.
- **Hatalar** (`APIError`): API'nin problem gövdesi (`status`, `code`, Türkçe `title`, 429'da `Retry-After`), `unreachable`,
  `cancelled`, `invalidResponse`. Ekrandaki metin `APIError.message`: API başlıkları zaten Türkçe; 429 →
  "Çok fazla deneme; N sn sonra tekrar deneyin."

### Çalıştırma

Simülatör Mac'in ağını kullanır; `http://localhost:5248` Mac'teki API'dir.

```sh
dotnet run --project src/BankingAI.Api --launch-profile http
open ios/BankingAI/BankingAI.xcodeproj     # Xcode'da iPhone 17 Pro seç, ▶ Run
xcodebuild -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI \
  -destination 'platform=iOS Simulator,name=iPhone 17 Pro,OS=26.5' test
```

Demo kullanıcıları `ada`, `deniz`, `ege`; şifreler yalnız yerel `.env`'de (`BANKINGAI_DEMO_PASSWORD_*`).

### Test ve doğrulama

34 test durumu (APIClient 18, Login 7, Home 4, SessionStore 4, Keychain 1). 7 kasıtlı bozma yakalandı: token gönderilmez, giriş
401'i oturumu kapatır, `Retry-After` okunmaz, süresi geçmiş oturum açılışta kalır, şifre alanda kalır, boşluklu kullanıcı adı
kabul edilir, 401 oturumu kapatmaz. Elle (simülatör, gerçek API): yanlış şifre mesajı, doğru girişte "Merhaba, Ada!", uygulama
kapanıp açılınca oturum sürüyor, Çıkış giriş ekranına döndürüyor.

### Sınırlar

- Yalnız simülatör: gerçek iPhone için API'nin ağa açılması ve HTTPS gerekir (OPS-04).
- Ana ekran geçici; sohbet ekranı AI-08 (iOS-b), hesap ekranları IOS-04…
- URLSession'ın varsayılan bekleme süresi (60 sn) kullanılıyor; sohbet çağrısı için süre AI-08'de ele alınır.
