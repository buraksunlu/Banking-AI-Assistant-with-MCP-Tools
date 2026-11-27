# Banking AI Assistant

**An AI spending assistant for a banking app, where the LLM chooses tools but never does the math.**

Ask questions in natural Turkish such as *"How much did I spend this month?"*, *"How much have I paid Starbucks in the last three months?"* or *"What are my recurring payments?"*. A local LLM figures out what you mean and picks the right tool. The .NET backend computes every number and checks every figure in the answer against the tool output.

![.NET 10](https://img.shields.io/badge/.NET_10-512BD4?style=flat&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-512BD4?style=flat&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat&logo=postgresql&logoColor=white)
![Python](https://img.shields.io/badge/Python-3776AB?style=flat&logo=python&logoColor=white)
![FastAPI](https://img.shields.io/badge/FastAPI-009688?style=flat&logo=fastapi&logoColor=white)
![LangChain](https://img.shields.io/badge/LangChain-1C3C3C?style=flat&logo=langchain&logoColor=white)
![Ollama](https://img.shields.io/badge/Ollama-000000?style=flat&logo=ollama&logoColor=white)
![SwiftUI](https://img.shields.io/badge/SwiftUI-F05138?style=flat&logo=swift&logoColor=white)

> All data in this project is **synthetic**. No real bank or customer data is used, and everything runs locally for free.

## Why it is built this way

LLMs are good at understanding a messy question. They are bad at arithmetic, and in banking a wrong number is not acceptable. So the responsibilities are split:

| The LLM (local Qwen via Ollama) | The .NET backend |
|---|---|
| Understands the question, including typos and relative dates | Is the financial source of truth |
| Picks one or more **read-only** MCP tools | Computes, filters and authorizes every figure |
| Writes the explanation around the numbers | Checks that every number in the answer came from a tool |
| Asks a clarifying question when the request is ambiguous | Takes the customer identity from the JWT, never from the request |

The model gets **no SQL, no database access, no write access and no money calculations**.

## Architecture

```text
iPhone (SwiftUI) ─► BankingAI.Api ─► ai/service (FastAPI, Qwen) ─► BankingAI.McpServer (read-only tools)

BankingAI.Api ───────┐
BankingAI.McpServer ─┴► BankingAI.Hosting ─► BankingAI.Application ─► BankingAI.Data ─► PostgreSQL
BankingAI.Cli ───────────────────────────────────────────────────────► BankingAI.Data
```

| Project | Role |
|---|---|
| `src/BankingAI.Data` | EF Core DbContext, entities, migrations, idempotent seed data, validation |
| `src/BankingAI.Application` | Account services and deterministic analytics |
| `src/BankingAI.Api` | ASP.NET Core REST API (JWT, ProblemDetails, OpenAPI 3.1 + Scalar) |
| `src/BankingAI.McpServer` | Read-only Model Context Protocol server with 20 tools, bound to `127.0.0.1` only |
| `src/BankingAI.Hosting` | Shared setup for the API and MCP server: JWT validation, JSON rules, read-only services |
| `src/BankingAI.Cli` | Migrations, seeding, market-data import, password management |
| `ai/service` | Python AI service (FastAPI + LangChain + Ollama): chat flow, MCP client, answer cards |
| `ai/model_check` | Evaluations against the real model: tool selection, typo tolerance, answer accuracy |
| `ios/BankingAI` | SwiftUI iPhone app |

## Features

**Assistant**
- Free-form Turkish questions answered as cards with figures and links to the matching transactions
- Clarifying questions when a request is ambiguous, follow-up questions, date expressions, typo tolerance
- 20 read-only MCP tools covering spending, payments, planning, income, "what if" scenarios, accounts and transaction search

**Analytics (deterministic, no LLM)**
- Spending summary, category and merchant breakdowns, largest transactions
- Period comparison and monthly trends
- Recurring and upcoming payment detection
- Budget check with a month-end forecast, savings simulation ("what if I cut 20%?"), income share
- "What if" scenarios: what past spending would be worth in USD, EUR or gram gold, using two years of daily central-bank rates

**API and data integrity**
- JWT authentication with per-IP login rate limiting. Another customer's account returns 404, never 403.
- `Idempotency-Key` required on money-moving requests, atomic balance updates, 409 on insufficient funds
- Cursor-based pagination and a consistent ProblemDetails error format with machine-readable codes
- Four layers of validation: input rules, entity checks, a `SaveChanges` guard, and PostgreSQL `CHECK`, unique and foreign-key constraints

**iPhone app**
- Login (Keychain), dashboard, accounts, transactions, category and monthly charts, chat, profile

## Testing

| Suite | Tests |
|---|---|
| .NET: Data / Application / API / MCP server (xUnit) | 223 / 511 / 624 / 222 |
| Python AI service (without the model) | 1,969 |
| iOS (Swift Testing) | 438 |

Highlights: OpenAPI contract snapshot tests, HTTP integration tests with `WebApplicationFactory`, MCP tests through a real C# MCP client, and expected values that come from hand-calculated fixtures rather than from the seeder. Parallel PostgreSQL test runs are isolated with advisory locks.

**Model evaluation (`qwen3.5:4b`):** tool selection 144/144, free-form wording 0 errors in 80 cases, misspelled input 0 errors in 116 cases, answer accuracy 33/33.

## Getting started

**Requirements:** .NET 10 SDK, Docker (PostgreSQL), Python 3.14, [Ollama](https://ollama.com) with `qwen3.5:4b`, and Xcode for the iPhone app.

```sh
cp .env.example .env        # fill in the values, then load them in each new terminal:
set -a; source .env; set +a

# Database: apply migrations, then run the seed commands listed in README.tr.md (in order)
dotnet run --project src/BankingAI.Cli -- migrate

# API on http://localhost:5248 (interactive docs at /scalar)
dotnet run --project src/BankingAI.Api --launch-profile http

# Or start everything (PostgreSQL, Ollama, API, MCP server, AI service) at once
scripts/demo.sh
```

Run the tests:

```sh
dotnet test BankingAI.slnx
ai/.venv/bin/python -m pytest ai -k "not real"
```

The full seed list, environment variables and endpoint table are in the Turkish guide [README.tr.md](README.tr.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/api.md](docs/api.md) | API contract, status codes, sample JSON |
| [docs/security.md](docs/security.md) | Login, JWT, how customer context reaches the AI and MCP layers |
| [docs/mcp.md](docs/mcp.md) | Read-only MCP server: boundary, tools, argument checks, error codes |
| [docs/local-ai.md](docs/local-ai.md) | Model choice, Ollama connection, AI service contract |
| [docs/analytics.md](docs/analytics.md) | Analytics contracts and acceptance examples |
| [docs/database-design.md](docs/database-design.md) | Tables, relationships, seed and import boundaries |
| [docs/input-validation.md](docs/input-validation.md) | Validation rules and transaction kinds |
| [docs/ios.md](docs/ios.md) | iPhone app structure and conventions |
| [docs/openapi/v1.json](docs/openapi/v1.json) | Committed OpenAPI 3.1 contract |

Detailed docs are written in Turkish.

---

Built by [Burak Ünlü](https://www.linkedin.com/in/burakunlu-dev) · Software Engineer
