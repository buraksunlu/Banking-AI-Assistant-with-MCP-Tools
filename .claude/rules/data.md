---
paths:
  - "src/BankingAI.Data/**"
  - "src/BankingAI.Cli/**"
  - "src/BankingAI.Application/Transactions/**"
  - "tests/BankingAI.Data.Tests/**"
  - "docs/database-design.md"
  - "docs/synthetic-seed.md"
  - "docs/input-validation.md"
---

# Veri katmanı: doğrulama, işlem yazma, seed, migration

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

### İşlem yazma

`Transaction` doğrudan `db.Add` ile eklenmez:

- `Writing/TransactionWriter` (API-11'e kadar `Seeding/TransactionSeedWriter`): tek bacak; insert +
  `ExecuteUpdate(Balance + delta)` aynı DB transaction'ında. `PairId`'li işlemi reddeder. Seed'ler ve API (API-11)
  kullanır. Kendi transaction'ını yönetir: açık transaction'lı context'i reddeder, bu yüzden testler rollback yapamaz.
  `WriteOutcome` döner (API-12): aynı kimlik + aynı olay (`TransactionWriter.SameEvent`: istemcinin söylediği alanlar;
  zaman/kategori hariç) → `AlreadyExisted`, bakiye ikinci kez değişmez; farklı olay → `IdempotencyConflictException` (409).
  Eşzamanlı tekrar birincil anahtara (23505) takılır, geri alınıp aynı karşılaştırılır.
- `Writing/PairedTransactionWriter`: iki bacak + iki bakiye tek transaction'da; bakiyeler
  **her zaman küçük AccountId önce** güncellenir (aksi halde ters yönlü transferler 40P01 deadlock alır).
  Aynı PairId tekrarı (API-12, KIND-03 kararı): aynı iki bacak → `AlreadyExisted`, farklı → çakışma.
- Bakiye mantığı `TransactionWriter.ApplyBalanceDeltaAsync` içinde ortaktır. Bakiye 0'ın altına ya da para üst
  sınırının üstüne çıkacaksa `BalanceLimitException` (InvalidOperationException'dan türer) atılır ve hiçbir şey
  yazılmaz; API bunu 409'a çevirir. Ön bakiye kontrolü yapılmaz: tek karar veren atomik güncellemedir.
- API'den yazma: `Application/Transactions/TransactionWriteService` (`ITransactionWriteService`; yalnız Purchase/Salary,
  yön türden, para birimi hesaptan, zaman `TimeProvider`'dan; Business hesap da kabul edilir — kullanıcı kararı 3-B).
  Saat entity kurulmadan **önce** okunur: entity'nin `CreatedAt` varsayılanı kurulurken alınır ve yazıcı
  `OccurredAt > CreatedAt`'i reddeder. LLM/MCP'ye yazma verilmez. `Idempotency-Key` (API-12) işlem eklemede zorunlu,
  hesap açmada isteğe bağlı; kayıt kimliği `IdempotentId.For(kaynak, müşteri, anahtar)` ile türetilir, tekrar
  `Idempotent-Replayed: true` başlığıyla aynı 201'i alır. Testlerdeki POST yardımcıları her çağrıda yeni anahtar üretir.

### Seed'ler

Sabit GUID'ler: müşteri `10000000-…-000000000001..3` (Ada Kahve, Deniz Market, Ege Yolcu), hesap
`20000000-…-00000000000N` (7 = Deniz'in Business "Kahve Durağı" hesabı), işlem
`30000000-…-{ordinal:D12}`, çift `40000000-…`. Demo hesap numaraları sabit (`DUMMY-…`); API'den açılan hesaplar
`AccountNumberSequence` sayacından `ACC-n` alır (sayaç rollback'te geri dönmez, boşluk normaldir). Kullanılan ordinal aralıkları: 1–14, 101–106,
201–206, 301–306, 401–402, 501–536, 601–628, 701–718; SYN-15 gerçekçi yıl: müşteri …0004–0009 (Selin, Murat, Zeynep, Hasan,
Elif — Europe/Berlin, Can), hesap …0008–0016 (16 = Can'ın Business dükkânı), ordinal blokları 11001+…16001+ ve çiftler 19001+ (PairId
…0101+). **Ada/Deniz/Ege elle hesaplanmış demodur, yeni veri onlara yazılmaz**; bütün veri setini sayan testler ilk demo hesaplarına
(…0001–0007) daraltılmıştır. Yeni personaların arka planı sabit seed'li üreticiden gelir: Credit'ler elle, Debit/sayılar altın değer
(`RealisticDatasetTests`; üretici değişince bilerek yenilenir). Seed'ler idempotenttir; aynı ID farklı
içerikle bulunursa sessizce düzeltmez, hata verir. Transaction seed'leri advisory lock kullanır
ve liste **sırasıyla** yazılır (boş hesaba Credit'ler Debit'lerden önce gelmeli).

## CLI ve seed komutları

Console CLI (`src/BankingAI.Cli`), seed sırası önemlidir:

```sh
dotnet run --project src/BankingAI.Cli -- migrate
dotnet run --project src/BankingAI.Cli -- seed categories        # sonra: market-assets, customers, accounts
dotnet run --project src/BankingAI.Cli -- seed transactions credits
# diğer transaction scope'ları: subscriptions, category-examples, grocery-growth,
# categorization-boundaries, recent-spending, demo-coverage, pairs, realistic (SYN-15: 6 persona, 1 yıl, 1.710 işlem)
dotnet run --project src/BankingAI.Cli -- users set-password <username> [--customer <customer-id>]   # gizli sorar
```

## Migration komutları

Migration:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project src/BankingAI.Data --startup-project src/BankingAI.Cli
dotnet tool run dotnet-ef migrations script <PreviousMigration> <Name> --project src/BankingAI.Data --startup-project src/BankingAI.Cli
docker exec bankingai-week1-postgres-1 psql -U bankingai_foundation -d bankingai_foundation -c "<SQL>"
```
