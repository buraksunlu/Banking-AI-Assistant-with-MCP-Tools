---
paths:
  - "src/BankingAI.McpServer/**"
  - "tests/BankingAI.McpServer.Tests/**"
  - "docs/mcp.md"
---

# MCP server (src/BankingAI.McpServer)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

`BankingAI.McpServer` (docs/mcp.md): ayrı süreç, `127.0.0.1:5249/mcp`, durumsuz Streamable HTTP, `ModelContextProtocol.AspNetCore`
2.2.0. Araçlar `Tools/SpendingTools` + `Tools/PaymentTools` + `Tools/PlanningTools` (bütçe, tasarruf) + `Tools/IncomeTools` (gelir payı,
öneri; yalnız tam ay `ToolCompleteMonth`) + `Tools/InvestmentTools` (iki "ne olurdu"; INC-03), 15 araç (servisler **kurucudan**; SDK metot parametresinde servis tahminini bazen yanlış yapıyor),
argümanlar `ToolCallFilter`'da yayınlanan şemaya göre (SDK şemayı uygulamaz), hatalar `ToolErrors` ile
`{code: invalid_arguments|session_invalid|internal_error, message}`. Yeni araç → `ToolDiscoveryTests` allowlist'ine satır. Filtre tam
sayıyı da uygular (SDK `"3"`/`3.5`'i sessizce 3 yapar), tutarı (`number`: JSON sayısı; `"1000"` reddedilir), bayrağı (`boolean`: yalnız JSON true/false) ve isteğe bağlı parametrenin
`["string", "null"]` tipini (`null` = verilmedi); `[Range]` şemaya `minimum`/`maximum` olarak, varsayılan değerli parametre zorunsuz yazılır.
