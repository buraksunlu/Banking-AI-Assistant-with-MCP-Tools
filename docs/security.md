# Güvenlik kararları

## SEC-01 — Müşteri bağlamı ve giriş (12 Ekim 2026, kullanıcı kararları)

Analytics servisleri bir `CustomerId` ister. Bu kimlik istemcinin veya LLM'in **değiştiremeyeceği**
bir yerden gelmelidir; istekte parametre olarak alınmaz (RULE.md §15, SEC-01).

Seçilen yol: **kullanıcı adı + şifre ile gerçek giriş ve imzalı JWT.** Demo farklı müşteri hesaplarıyla
yapılacağı için şifresiz "müşteri seç" girişi reddedildi. Header'da müşteri kimliği (herkes istediğini
yazabilir) ve tam kimlik sistemi (ASP.NET Identity tabloları) kapsam dışıdır.

### SEC-01a — Kimlik bilgileri (uygulandı)

- `CustomerCredentials` tablosu (ayrı tablo, kullanıcı onayıyla): `CustomerId` (PK + FK, müşteri başına
  tek giriş), benzersiz `Username`, `PasswordHash`, `CreatedAt`, `UpdatedAt`. Migration `AddCustomerCredentials`.
- Kullanıcı adı: küçük harf, rakam, nokta, tire, alt çizgi; 3–50 karakter; girişte kırpılıp küçük harfe çevrilir.
  Müşteri numarası (CUST-n) kullanılmaz, çünkü DB sırasından üretilir ve yeniden kurulumda değişir.
- Şifre **saklanmaz**: ASP.NET Core `PasswordHasher` (PBKDF2, şifre başına rastgele tuz) özeti tutulur.
  Şifre 8–128 karakter.
- `UpdatedAt`, `CreatedAt` gibi `SaveChanges` tarafından sunucu saatiyle atanır.
- Şifreyi belirleme yolu CLI'dır: `users set-password <kullanıcı-adı> [--customer <müşteri-id>]`.
  Terminalde gizli ve iki kez sorar; borudan iki satır okur. Şifre ekrana veya loga yazılmaz.
  Çakışmalar (kullanıcı adı başka müşteride, müşterinin başka kullanıcı adı var, müşteri yok) hatadır.
- Demo kullanıcıları `ada`, `deniz`, `ege`; şifreleri rastgele üretilmiş ve yalnız yerel, git'e girmeyen
  `.env` içindedir (`BANKINGAI_DEMO_PASSWORD_*`). Repo, testler ve dokümanlar bu değerleri içermez;
  testler her çalıştırmada kendi geçici test kullanıcısını üretir ve rollback eder.

### SEC-01b — API girişi (uygulandı, 12 Ekim 2026)

- `POST /api/auth/login { username, password }` → `200 { token, expiresAt }`. Token HS256 imzalı JWT'dir;
  içinde yalnız `sub` (müşteri kimliği), issuer `BankingAI.Api`, audience `BankingAI` ve süre bilgisi vardır.
  Süre 60 dakika; `ClockSkew = 0` (varsayılan 5 dk tolerans token'ı fiilen 65 dk yapardı).
- Yanlış kullanıcı adı ve yanlış şifre **aynı** 401 cevabını verir. Bilinmeyen kullanıcı adında da sahte bir
  özete karşı PBKDF2 çalıştırılır; böylece cevap süresi kullanıcı adının var olup olmadığını ele vermez
  (`CredentialStore.VerifyAsync`). Eksik/boş alan veya boş gövde 400'dür.
- Aynı IP'den dakikada en fazla 5 giriş denemesi (başarılılar dahil); fazlası 429. ASP.NET'in hazır
  `FixedWindow` sınırlayıcısı, yalnız bağlantının IP'si (`X-Forwarded-For` güvenilmez).
- İmza anahtarı `BANKINGAI_JWT_SIGNING_KEY` ortam değişkenindedir (yerelde `.env`, rastgele 48 bayt base64).
  Yoksa veya 32 bayttan kısaysa API açılmaz.
- **Varsayılan olarak her endpoint token ister** (`FallbackPolicy = RequireAuthenticatedUser`). Açık olanlar
  `[AllowAnonymous]` ile işaretlenir: `GET /api/health`, `POST /api/auth/login`, geliştirmede `/openapi/v1.json`.
  Bir endpoint'te işaret unutulursa kapalı kalır; tokensız istekte bilinmeyen route da 401 alır (hangi
  route'ların var olduğu sızmaz).
- Controller'lar müşteriyi yalnız `User.GetCustomerId()` ile token'dan okur.
- `GET /api/me` → `{ username, customerNumber, firstName, lastName, preferredCurrency, timeZone }`. İç müşteri
  kimliği (GUID) bilerek cevapta yoktur; istemcinin ona ihtiyacı yok ve istekte parametre olarak gönderilmesi
  özendirilmemeli. Token geçerli ama giriş artık yoksa 401.

Bilinen sınırlar: logout/token iptali ve refresh token yok (token 60 dk geçerli kalır); hesap kilitleme
sonraya bırakıldı (kullanıcı kararı); sınır sayaçları bellektedir (yeniden başlatmada sıfırlanır, tek sunucu);
proxy arkasında bütün istemciler tek IP görünür ve HTTP'de token açıkta gider (ikisi de OPS-04).

### AI / MCP'ye müşteri bağlamı (tasarım; MCP adımında uygulanacak)

```text
SwiftUI ──(Bearer token)──► API ──(aynı token, HTTP header)──► FastAPI ──(header)──► MCP Server
```

- MCP tool'larında `customerId` parametresi **yoktur**; MCP server müşteriyi header'daki token'dan çözer.
- Token LLM prompt'una veya tool argümanlarına girmez. Prompt injection ("Deniz'in hesabını göster")
  olsa bile modelin başka bir müşteriyi seçecek aracı yoktur.
