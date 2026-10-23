---
paths:
  - "tests/**"
---

# Testler (tests/)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

Testlerin ortak yardımcıları `tests/Shared` (`TestTokens`, `CapturedLogs`, `CommittedRowsLock`), `BankingAI.TestSupport`
namespace'i proje dosyasında global using.


- `BankingAI.Data.Tests`: PostgreSQL testleri `[Collection(PostgreSqlCollection.Name)]` ile sıralı
  çalışır; ya kendi transaction'ında rollback yapar ya da geçici `DUMMY-*TEST-*` hesap açıp siler. Geçici
  hesaplar sabit demo müşterisine (Ada/Deniz) açılır; "ilk müşteri"yi seçme (başka test projesinin geçici
  müşterisi olabilir) ve `EmptyCustomer`'a hesap açma (analytics testleri onu hesapsız bekler).
- **Yeni endpoint eklenince** `tests/BankingAI.Api.Tests/EndpointSweepTests.cs`'teki örnek istek tablosuna geçerli bir
  istek eklenir (API-14); eklenmezse kapsam bekçisi kırmızı olur. Tablo token zorunluluğunu ve var olmayan müşteride
  `session_invalid`'i bütün korumalı endpoint'lerde tarar; yazma örnekleri yalnız var olmayan müşteri için gönderilir.
- **Sözleşme (API-15):** `docs/openapi/v1.json` commit'lenmiş OpenAPI belgesidir; `OpenApiContractTests` üretilenle birebir
  karşılaştırır. Endpoint, DTO, parametre ya da cevap kodu değişince test kırmızı olur; bilerek değiştiyse
  `BANKINGAI_UPDATE_OPENAPI=1 dotnet test tests/BankingAI.Api.Tests --filter "FullyQualifiedName~The_document_is_the_same"`
  ile yenile ve dosyayı commit'e ekle. Belge kuralları `Api/OpenApi/ApiDocument`'ta; `AddOpenApi` lambda biçiminde kalmalı
  (XML yorum üreticisi metot adını yakalamaz). Para JSON sayısıdır (`JsonNumberHandling.Strict`; tırnaklı tutar 400).
- `BankingAI.Api.Tests`: `DemoData.cs` Application.Tests'ten link'lenir (kopyalanmaz); `TestApi.JsonOptions` (API'nin JSON ayarları, DB'siz); `TestApi.Create(...)` (WebApplicationFactory + ayar ezme), `TestTokens` (sahte/süresi
  geçmiş token), `TestUser` (commit edilen geçici "Test Api" müşterisi + `apitest.*` girişi, dispose'da silinir,
  10 dk'dan eski kalıntılar temizlenir; silme sırası işlemler → hesaplar → giriş → müşteri, müşterinin bütün hesapları
  silinir). Yazma testleri demo müşterisine değil `TestUser`'a yapılır.
- **İşlem yazan testler (API-11) demo hesabını hiç hedef almaz, ret bekleyen testler dahil:** kasıtlı bozmada kontrol
  kalkınca yazma gerçekten olur. "Başkasının hesabı" testi ters kurulur (Ada'nın token'ıyla geçici hesaba yazma).
  Application.Tests'te bunun için `TemporaryCustomer` ("Test Application" müşterisi + hesapları, paylaşımlı kilit,
  dispose'da işlem → hesap → müşteri silinir) vardır.
- Ada/Deniz'e geçici hesap açan Data testleri paralel çalışır: Application/Api testleri müşterinin **bütün**
  hesaplarını listeleyip saymamalı (sabit hesap ID'leriyle seçmeli) ya da kilit kullanmalıdır.
- Test projeleri `dotnet test BankingAI.slnx`'te **paralel** çalışır. Commit eden testler
  `tests/Shared/CommittedRowsLock.SharedAsync()`, müşteri ayırmadan satır sayan/toplayan testler `ExclusiveAsync()` alır
  (PostgreSQL advisory lock 2026101201; dosya üç test projesine `<Compile Include … Link>` ile bağlı). Sınıf boyunca
  gerekiyorsa `IAsyncLifetime` ile (yazıcı testleri paylaşımlı, `SyntheticDatasetIntegrityTests` özel). **Bir test en
  fazla bir kilit alır:** ikinci paylaşımlı istek, ilkini bekleyen bir özel isteğin arkasında kuyruğa girer (kilitlenme).
- `BankingAI.Application.Tests`: seed edilmiş demo DB'yi okur. Demo müşteri/hesap GUID'leri ve
  `DemoNowUtc` tek yerde, `DemoData.cs` içindedir (`using static ...DemoData;`); test dosyasına kopyalama.
- Beklenen değerler **elle hesaplanmış fixture**'lardır; seeder'dan türetilmez
  (`SyntheticDatasetIntegrityTests`, `docs/synthetic-seed.md`). Seed eklemek bakiye/sayı fixture'larını değiştirir.
- Mevcut davranışı kapsayan yeni testler yeşil başlarsa, kodu geçici bozup (ör. sınırda `<` → `<=`)
  kırmızıyı gör, sonra geri al. Değişikliği dosyanın kendisinde yap ve `git checkout -- <dosya>` ile
  geri al; `cp`/`mv` ile yedek dosya kullanma (proje klasörüne fazladan `.cs` kopyası derlemeyi bozar).
  Dosyada commit edilmemiş yeni kod varsa (TDD adımında normal durum) `git checkout` onu da siler: o zaman
  bozmayı tersine çevrilebilir tek düzenlemeyle yap ve geri dönüşü `shasum` önce/sonra karşılaştırmasıyla doğrula.
  Proje 13 Ekim 2026'da iCloud'lu Masaüstünden `~/Projects/VAKIFBANK/AI_Assistant`'ye taşındı; iCloud `.git`
  nesnelerini buluta alıp git'i kilitliyordu. Depoyu iCloud/eşitlenen klasöre geri koyma. Sonunda `dotnet build --no-incremental` ile
  doğrula; eski dosya zamanı MSBuild'in bozuk derlemeyi kullanmasına yol açabilir.
- Yeni enum kolonunda EF migration'ı `NOT NULL DEFAULT 0` üretir: elle nullable ekle → backfill →
  belirsiz kayıtta `RAISE EXCEPTION` → `NOT NULL` yap. DB varsayılanı olmadığı için testlerdeki ham
  `INSERT`'ler `Kind` ve `AccountType`'ı açıkça vermelidir.
