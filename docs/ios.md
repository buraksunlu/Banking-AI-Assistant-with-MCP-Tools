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
    Home/        HomeView, HomeViewModel (ana ekran: "Merhaba, Ada!", "Asistana sor", Çıkış)
    Chat/        ChatView, ChatViewModel, MessageBubble, ChatMessage (AI-08)
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
- Hesap ekranları IOS-04… (ana ekrana eklenecek).
- Sohbet çağrısı dışındaki istekler URLSession'ın varsayılan bekleme süresini (60 sn) kullanır.

## iOS-b: sohbet ekranı (AI-08; 20 Ekim 2026)

Backend değişmedi; iPhone mevcut iki uç noktayı kullanır: `GET /api/assistant/status` ve `POST /api/assistant/messages`.

```text
HomeView ──"Asistana sor"──► ChatView ──► ChatViewModel ──► APIClient ──► .NET API ──► AI servisi ──► MCP + Ollama
 (@State chat: ChatViewModel; ana ekranla yaşar, çıkışta biter)
```

- **Yer (K11-B):** ana ekranda "Asistana sor" düğmesi, sohbet NavigationStack ile itilir. Hesap ekranları ileride ana ekrana gelir.
- **Geçmiş (K4-D):** yalnız bellekte. `ChatViewModel` `HomeView`'un `@State`'inde durur: geri dönüp girince konuşma yerindedir; çıkış
  yapınca ana ekranla birlikte silinir. Diske yazılmaz.
- **Gönderme:** tek seferde bir soru (tek yerel model sırayla cevaplar; API dakikada 10 mesaj). Metin kırpılır; boş ya da 500 karakteri
  aşan mesaj gönderilmez (API gibi Unicode karakter sayılır).
- **Durdur (K5-C):** bekleyen isteği iptal eder; ekrandan çıkınca ya da uygulama arka plana geçince de iptal. Soru "Durduruldu" +
  "Tekrar gönder" olur, hata gösterilmez. Model arka planda turu yine bitirir (sınır).
- **Hatalar (K6-A, `ChatViewModel.failed`):** 503 → turuncu "Asistan şu an kullanılamıyor" bandı, yazı alanı kapanır, "Yeniden kontrol et"
  durumu yeniden sorar; 504/502 → balonun altında API'nin Türkçe başlığı + "Tekrar dene"; 429 → "N sn sonra"; 401 → oturum kapanır,
  giriş ekranı gelir (APIClient). "Tekrar dene" aynı balonu yeniden gönderir, sorunun ikinci kopyası oluşmaz.
- **Durum (K7-A):** ekran açılınca bir kez sorulur; başarısız kontrol durumu değiştirmez.
- **Bekleme süresi (K12-A):** yalnız sohbet isteğinde 40 sn (`APIRequest.timeout`). Zincir AI servisi 30 → .NET 35 → iPhone 40: API'nin
  açıklayıcı 504'ü her zaman önce gelir.
- **Cevap:** satır içi Markdown (K8-A, `inlineOnlyPreservingWhitespace`: kalın/italik/bağlantı, satır sonları korunur); bozuk Markdown
  düz metin olarak gösterilir. `Answer`, `Clarification`, `OutOfScope` aynı balonda. API'nin gönderdiği araç ve rakam kalemleri çözülmez
  (K13-A; "nasıl hesaplandı" paneli IOS-11).
- **Boş sohbet (K9-A):** üç örnek soru, dokununca gönderilir (kapsam dışı cevabının önerdiği sorularla aynı). CHAT-06'dan beri bunlar
  yalnız yedek: önce sunucunun başlangıç soruları istenir (aşağıda "Önerilen sorular").

### Test ve doğrulama (AI-08)

54 test durumu (önceki 34 + `ChatViewModelTests` 20: kırpma ve 500 sınırı, boş/uzun mesaj, beklerken ikinci mesaj, Durdur, 503 bandı ve
geri gelme, zaman aşımı, 429, 401, aynı balonda yeniden deneme, açılıştaki durum, cevap türleri, Markdown kalın/bozuk, örnek sorular,
yalnız sohbet çağrısının 40 sn beklemesi). 8 kasıtlı bozma yakalandı (aralarında: Durdur iptal etmez, 503 bandı açmaz, "Tekrar dene" yeni
balon ekler, beklerken ikinci gönderim engellenmez, mesaj kırpılmaz, bozuk Markdown metni yutar).

Elle (simülatör; API + MCP server + AI servisi + Ollama açık, `BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z`): örnek soruya kalın rakamlı
cevap; Durdur → "Durduruldu", "Tekrar gönder" aynı balonda cevap alır; geri dönüp girince geçmiş duruyor, beklerken çıkınca soru
durduruluyor; AI servisi kapalıyken bant + kapalı yazı alanı + pasif "Tekrar dene", servis açılınca "Yeniden kontrol et" bandı kaldırıyor ve
"Tekrar dene" cevap alıyor; çıkış ve yeniden girişte sohbet boş.

### Sınırlar (AI-08)

- Durdur yalnız iPhone'un beklemesini bırakır; .NET ve AI servisi turu bitirir (iptal zincire taşınmıyor).
- Takip sorusu yok ("peki geçen ay?", CHAT-02); her mesaj bağımsız.
- Kaynak ve rakam paneli yok (IOS-11).
- Soğuk model: model 5 dk kullanılmayınca bellekten çıkar; harici SSD'den yüklenirken ilk soru 504 alabilir ("Tekrar dene" çalışır).
- Akarak gösterme (stream) yok: cevap tek seferde gelir.

## iOS-c: önerilen sorular (CHAT-06 PR2; 22 Ekim 2026)

Arka uç (PR1, docs/local-ai.md "Önerilen sorular") her açıklanmış cevapla `suggestions` ve sohbet açılışı için
`GET /api/assistant/suggestions` veriyor; iPhone bunları yalnız gösterir ve dokunulanı **metni aynen** gönderir (sunucu o metni tanıyıp
aracı model seçmeden çalıştırır). Hangi sorunun geleceğine iPhone karar vermez.

```text
ChatView ──dokunma──► ChatViewModel.send(metin) ──► APIClient ──► POST /api/assistant/messages
   ▲ suggestions (son cevabın altında) / starters (boş sohbet)       ◄── reply.suggestions
   └── .task { open() } ──► GET /api/assistant/status  +  (sohbet boşsa) GET /api/assistant/suggestions
```

Kullanıcı kararları:

- **K1-B — tek liste:** `ChatViewModel.suggestions` her cevapta yenilenir, bir şey gönderilir gönderilmez boşalır (sunucu da teklifini
  yalnız bir sonraki mesajda tanır). Hata, Durdur ve geri soruda liste boş.
- **K2-A — son balonun altında:** alt alta küçük düğmeler (`.bordered`, `.small`), yalnız son mesaj asistanınsa; beklerken pasif.
- **K3-B — başlangıçlar yalnız boş sohbette**, durum kontrolüyle yan yana (`open()`); geçmiş varsa istek gitmez.
- **K4-B — yüklenirken gösterge, olmazsa yedek:** `starters` boşken `ProgressView`; hata (503/504/bozuk) ya da boş liste → sabit üç örnek.
  Başlangıç hatası bandı açmaz; bandı yalnız durum kontrolü açar.
- **K5-A — yedek örnekler** arka ucun kapsam dışı cevabındaki üç soru (her müşteri cevap alır; eski listedeki Starbucks sorusu çıktı).
- **K6-A — `suggestions` zorunlu:** alanı olmayan cevap "Beklenmeyen bir cevap alındı" + "Tekrar dene" (sessizce "öneri yok" sayılmaz).

### Test ve doğrulama (CHAT-06)

66 test durumu (önceki 54 + `ChatSuggestionTests` 12: cevabın önerileri, dokunulan metnin aynen gitmesi, gönderince boşalması,
hata ve geri soruda boş liste, alanı olmayan cevap, boş sohbette başlangıç + durum, dört yedek durumu, geçmişte başlangıç isteği yok).
6 kasıtlı bozma yakalandı (gönderince temizlememe, öneri koymama, geçmişte de isteme, yedeksiz bırakma, alanı isteğe bağlı yapma,
Durdur'un iptal etmemesi).

**Bulgu (kararsız test):** `stoppingCancelsTheRequestAndShowsNoError` yeni testlerle birlikte yaklaşık her iki koşuda bir kırmızıydı.
URLSession iptali uydurma sunucuya kendi kuyruğunda, görev hata verdikten biraz sonra bildiriyor; test sayacı hemen okuyordu. Test artık
isteğin sunucuya ulaşmasını ve iptal sayacını kısa süre (`waitUntil`, ≤ 2 sn) bekliyor; düzeltmeden sonra 10/10 ve bozmayı hâlâ yakalıyor.
Ürün kodu değişmedi.

### Sınırlar (CHAT-06)

- Öneriler yalnız son cevabın altında; eski cevaplarınkiler kaybolur.
- Başlangıç soruları ekran her boş açıldığında yeniden istenir.
- Öneri düğmeleri erişilebilirlikte normal düğme olarak okunur.
