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

### SEC-01b — API girişi (tasarım; sonraki adım)

- `POST /api/auth/login { username, password }` → `{ token, expiresAt }`; token süresi 60 dakika.
- Yanlış kullanıcı adı ve yanlış şifre **aynı** 401 cevabını verir (kullanıcı adı tahmin edilemesin).
- Aynı IP'den dakikada en fazla 5 giriş denemesi; fazlası 429 (ASP.NET hazır rate limiter).
- İmza anahtarı `BANKINGAI_JWT_SIGNING_KEY` ortam değişkeninde; yoksa API açılmaz.
- Bütün iş endpoint'leri token ister; `CustomerId` yalnız token'dan okunur. `/api/health` açıktır.
- İlk korumalı endpoint `GET /api/me`.
- Hesap kilitleme (N yanlış denemeden sonra kilit) kullanıcı kararıyla sonraya bırakıldı.

### AI / MCP'ye müşteri bağlamı (tasarım; MCP adımında uygulanacak)

```text
SwiftUI ──(Bearer token)──► API ──(aynı token, HTTP header)──► FastAPI ──(header)──► MCP Server
```

- MCP tool'larında `customerId` parametresi **yoktur**; MCP server müşteriyi header'daki token'dan çözer.
- Token LLM prompt'una veya tool argümanlarına girmez. Prompt injection ("Deniz'in hesabını göster")
  olsa bile modelin başka bir müşteriyi seçecek aracı yoktur.
