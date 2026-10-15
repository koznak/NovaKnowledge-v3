# NovaKnowledge

**Enterprise knowledge copilot** — a reference implementation of a grounding-first RAG platform
built on .NET 10 microservices, Azure OpenAI, Azure AI Search, and Microsoft Semantic Kernel, with a
Python evaluation service that computes real Ragas metrics.

This repository is a portfolio project. It is written to show how a production RAG system is
*assembled* — retrieval quality, grounded citations, agent orchestration, token/cost accounting,
failure handling, and honest evaluation — rather than to demonstrate a single prompt.

![Architecture](docs/images/architecture.svg)

---

## Scope & Limitations (read this first)

This is a **single-tenant demonstration with no authentication**. It is deliberately scoped so the
architecture is easy to review; it is **not** a hardened production deployment.

| Area | Status |
|------|--------|
| Authentication / authorization | **Not implemented.** No OAuth, API keys, or JWT validation on the APIs. |
| Tenancy | **Single configured tenant.** The data model is tenant-aware (`TenantId` columns and search filters), but `tenantId` is a configuration/request default (`"contoso"`), not an authenticated identity. |
| Evaluation | Real Ragas metrics when LLM credentials are present; otherwise a **deterministic fallback that is explicitly labeled `offline`**. |
| Cost figures | **Approximate**, derived from configurable per-token prices (`Pricing` config section). |
| Secrets | Committed config contains **placeholders only**. Real keys go in `dotnet user-secrets`. |

Adding authentication in front of the Gateway (Entra ID / OAuth) is described as a **production
extension** in [docs/copilot-studio-setup.md](docs/copilot-studio-setup.md) — it is not part of this build.

---

## What this demonstrates

- **Retrieval, not just generation.** Hybrid-capable search (vector + BM25 + semantic ranker;
  the shipped demo config runs keyword-only until an embedding model is configured) with
  tenant/department/docType filtering, deterministic cache keys, and OData-escaped filters.
- **Grounding you can verify.** Every answer carries citation DTOs; the prompt forces context-only
  answers, and a compliance agent revises for hallucination and adds a disclaimer.
- **Real agent orchestration.** A `ChatCompletionAgent` pipeline (Router → Research → Synthesis →
  Compliance) built on Microsoft Semantic Kernel, with per-step token and latency capture persisted
  as trace rows linked to the assistant message.
- **Real streaming.** Server-Sent Events emit model tokens as they arrive (`citations` → `token`* →
  `done`), with Prometheus-style metrics recorded per turn.
- **Honest evaluation.** A Python FastAPI service that runs Ragas faithfulness/answer-relevance/
  context-precision/context-recall against a golden Q&A set, and says clearly when it fell back to a
  heuristic.
- **Operational hygiene.** RFC 7807 error handling, split liveness/readiness probes, Polly retry +
  circuit breaker, structured logs, and OpenTelemetry traces/metrics.

---

## Architecture at a glance

```
Clients (Blazor Admin, REST, Copilot Studio)
    → YARP Gateway (:5000)
        → Chat.Api (:5001) · Orchestration.Api (:5002) · Search.Api (:5003)
        → Ingestion.Api (:5004) · Feedback.Api (:5005) · Admin.Api (:5006)
    → Ingestion.Worker (Service Bus) → Azure OpenAI + Azure AI Search
    → Blazor Admin UI (:5094) · Python Evaluation Service (:8000)
```

| Service | Port | Responsibility |
|---------|------|----------------|
| Gateway (YARP) | 5000 | Single entry point, routing |
| Chat.Api | 5001 | Sessions, SSE chat, citations, traces |
| Orchestration.Api | 5002 | Multi-agent pipeline |
| Search.Api | 5003 | Hybrid + semantic search |
| Ingestion.Api | 5004 | Upload, dedup, queue publish |
| Feedback.Api | 5005 | Ratings, JSONL export |
| Admin.Api | 5006 | Prompts, settings, metrics, eval trigger |
| Admin.Web | 5094 | Blazor admin UI |
| Evaluation Service | 8000 | Ragas metrics + chunking comparison |

See [docs/architecture/architecture-diagram.md](docs/architecture/architecture-diagram.md) for the
full set of diagrams.

---

## Prerequisites

- **.NET 10 SDK** (pinned by `global.json`)
- **Docker Desktop** (Postgres, Redis, Azurite, Service Bus emulator)
- **Python 3.12** (evaluation service)
- Optional: an **Azure subscription** with Azure OpenAI and Azure AI Search for the full hybrid path

## Quick Start

### 1. Start local infrastructure

```bash
docker compose up -d postgres redis azurite servicebus-sql servicebus-emulator
```

This starts Postgres (`5432`), Redis (`6379`), Azurite blobs (`10000`), and the Service Bus emulator.
Databases and seed data are created automatically on service startup.

### 2. Configure secrets (never commit them)

Committed `appsettings.json` files contain **placeholders**. Supply real values with user-secrets:

```bash
cd src/Gateway/NovaKnowledge.Gateway
dotnet user-secrets set "AzureSearch:Endpoint" "https://<your-search>.search.windows.net"
dotnet user-secrets set "AzureSearch:ApiKey" "<key>"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<your-chat>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>"
dotnet user-secrets set "AzureOpenAI:EmbeddingEndpoint" "https://<your-embed>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:EmbeddingApiKey" "<key>"
```

All projects share one user-secrets store (`novaknowledge-local-development`), so you set these
**once** from any project and every service picks them up. Alternatively, use an ignored
`appsettings.<Service>.local.json` override. See [SECURITY.md](SECURITY.md).

### 3. Run the services

```bash
dotnet run --project src/Gateway/NovaKnowledge.Gateway
dotnet run --project src/Services/Chat/NovaKnowledge.Chat.Api
dotnet run --project src/Services/Orchestration/NovaKnowledge.Orchestration.Api
dotnet run --project src/Services/Search/NovaKnowledge.Search.Api
dotnet run --project src/Services/Ingestion/NovaKnowledge.Ingestion.Api
dotnet run --project src/Services/Ingestion/NovaKnowledge.Ingestion.Worker
dotnet run --project src/Services/Feedback/NovaKnowledge.Feedback.Api
dotnet run --project src/Services/Admin/NovaKnowledge.Admin.Api
dotnet run --project src/Web/NovaKnowledge.Admin.Web
```

- Gateway Swagger: <http://localhost:5000/swagger>
- Admin UI: <http://localhost:5094>

### 4. Run the evaluation service

```bash
cd python/evaluation-service
python -m venv .venv
.venv\Scripts\activate           # Windows
# source .venv/bin/activate      # macOS/Linux
pip install -r requirements.txt
uvicorn main:app --reload --port 8000
```

---

## Running without Azure OpenAI (keyword-only mode)

You do **not** need Azure OpenAI *embeddings* to exercise retrieval. Keyword-only mode disables
query embeddings and relies on Azure AI Search BM25 + semantic ranker:

```jsonc
// appsettings.json (default in this repo)
"AzureSearch": { "EnableHybridSearch": false }
```

Per-request override:

```jsonc
{ "query": "PTO policy", "tenantId": "contoso", "topK": 5, "useHybrid": false }
```

**Trade-off:** no vector similarity, so recall on paraphrased/acronym queries is lower.

> **Still required:** an Azure AI Search resource (there is no local search emulator), and a chat
> model deployment for `/api/chat`. Keyword-only mode only removes the *embedding* dependency.

---

## API examples (via Gateway)

```bash
# Create a session
curl -X POST http://localhost:5000/api/chat/sessions \
  -H "Content-Type: application/json" -d '{"tenantId":"contoso"}'

# Stream a cited answer (Server-Sent Events)
curl -N -X POST http://localhost:5000/api/chat/sessions/<id>/messages \
  -H "Content-Type: application/json" -d '{"content":"What is the PTO policy?"}'

# Hybrid search
curl -X POST http://localhost:5000/api/search/query \
  -H "Content-Type: application/json" \
  -d '{"query":"PTO policy","tenantId":"contoso","topK":5}'

# Run the multi-agent pipeline directly
curl -X POST http://localhost:5000/api/orchestration/run \
  -H "Content-Type: application/json" \
  -d '{"message":"What is the BYOD policy?","tenantId":"contoso","conversationId":"00000000-0000-0000-0000-000000000000"}'
```

Each service also ships `.http` files (`NovaKnowledge.<Service>.http`) with these requests.

---

## Testing

```bash
# .NET: unit + integration (17 tests)
dotnet test NovaKnowledge.sln

# Python: evaluation service (8 tests)
pytest python/evaluation-service -q
```

The integration tests boot the real `Search.Api` composition root with `WebApplicationFactory` for
health/readiness/404 checks and exercise the ingestion pipeline with mocked Azure collaborators.

### Load testing (k6)

```powershell
.\scripts\run-load-test.ps1          # downloads k6 to tools/k6 if not on PATH
```

---

## Azure deployment

```bash
az deployment sub create \
  --location eastus2 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

---

## Documentation

- [Architecture diagrams](docs/architecture/architecture-diagram.md)
- [ADRs](docs/architecture/) — hybrid search, multi-agent, chunking
- [Portfolio overview](docs/portfolio-overview.md)
- [Demo script](docs/interview-demo-script.md)
- [Copilot Studio setup](docs/copilot-studio-setup.md)
- [Secret rotation & history scrub](docs/secrets-rotation.md)
- [Runbooks](docs/runbooks/)

## Contributing & security

- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md) — reporting, secrets handling

## License

[MIT](LICENSE)
