# NovaKnowledge

**Enterprise Knowledge Copilot Platform** — a production-grade Azure AI portfolio project for Contoso Corp.

NovaKnowledge demonstrates .NET 10 microservices, hybrid RAG, multi-agent orchestration, Azure AI Search, Azure OpenAI, Python ML evaluation, Copilot Studio integration, and full observability.

## Architecture

See **[Architecture Diagrams](docs/architecture/architecture-diagram.md)** for full system, microservices, RAG, chat, multi-agent, Azure deployment, and observability diagrams.

**Quick overview:**

```
Clients (Blazor Admin, Copilot Studio, REST)
    → YARP Gateway (:5000)
        → Chat.Api (:5001) · Orchestration.Api (:5002) · Search.Api (:5003)
        → Ingestion.Api (:5004) · Feedback.Api (:5005) · Admin.Api (:5006)
    → Ingestion.Worker (Service Bus) → Azure OpenAI + AI Search
    → Python Evaluation Service (:8000)
```

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- Azure subscription with:
  - Azure OpenAI (`gpt-4o-mini`, `text-embedding-3-large`)
  - Azure AI Search (Basic+ with semantic ranker for staging)
  - Azure Storage, Service Bus, PostgreSQL, Redis (or local emulators)

## Quick Start (Local)

1. **Start infrastructure:**
   ```bash
   docker compose up -d postgres redis azurite
   ```

2. **Configure secrets** — copy `appsettings.template.json` to each service's `appsettings.Development.json` and fill in Azure keys:
   ```json
   {
     "AzureOpenAI": { "Endpoint": "...", "ApiKey": "..." },
     "AzureSearch": { "Endpoint": "...", "ApiKey": "..." }
   }
   ```

3. **Run services** (separate terminals or use `dotnet run`):
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

4. **Python evaluation service** (use a venv to avoid global package conflicts):
   ```bash
   cd python/evaluation-service
   python -m venv .venv
   .venv\Scripts\activate          # Windows
   # source .venv/bin/activate     # macOS/Linux
   pip install -r requirements.txt
   uvicorn main:app --reload --port 8000
   ```

5. **Open Admin UI:** http://localhost:5xxx (see launchSettings) or Gateway swagger: http://localhost:5000/swagger

## API Endpoints (via Gateway)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/ingest/documents` | Upload document for indexing |
| POST | `/api/search/query` | Hybrid vector + keyword search |
| POST | `/api/chat/sessions` | Create chat session |
| POST | `/api/chat/sessions/{id}/messages` | Send message (SSE stream) |
| POST | `/api/feedback` | Submit feedback |
| GET | `/api/admin/metrics/usage` | Token/latency metrics |
| POST | `/api/orchestration/run` | Multi-agent orchestration |

## Azure Deployment

```bash
az deployment sub create \
  --location eastus2 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

## Testing

```bash
dotnet test
```

**Load tests (k6)** — start the Gateway first (`http://localhost:5000`):

```powershell
# Option A: wrapper script (downloads k6 to tools/k6 if not on PATH)
.\scripts\run-load-test.ps1

# Option B: install k6 globally (admin PowerShell)
choco install k6 -y

# Then run (from repo root)
k6 run tests/NovaKnowledge.LoadTests/load-test.js
```

Optional: `GATEWAY_URL=http://localhost:5000 .\scripts\run-load-test.ps1`

## JD Coverage

| Requirement | Implementation |
|-------------|----------------|
| Azure OpenAI | Chat + embedding services, prompt registry |
| Azure AI Search | Hybrid search + semantic ranker |
| RAG Pipeline | Ingestion worker: chunk → embed → index |
| Multi-Agent | Router → Research → Synthesis → Compliance |
| .NET Microservices | 6 APIs + Gateway + Worker |
| Python / Azure ML | FastAPI evaluation service + metrics |
| Copilot Studio | Custom connector docs in `docs/` |
| AI Ops | OpenTelemetry, Polly, cost metrics, k6 load tests |

Optional: `GATEWAY_URL=http://localhost:5000 .\scripts\run-load-test.ps1`

## Azure setup troubleshooting

### Keyword-only search (no embedding deployment)

You can run **without** an Azure OpenAI embedding deployment by using Azure AI Search text search only:

```json
"AzureSearch": {
  "EnableHybridSearch": false
}
```

This is already set in local `appsettings.json`. Search uses BM25 + optional semantic ranker on Azure AI Search — only your **Search API key** is required.

Per-request override:

```json
{ "query": "PTO policy", "tenantId": "contoso", "topK": 5, "useHybrid": false }
```

**Trade-off:** no vector similarity; quality is lower than full hybrid RAG. **Chat still needs** a `gpt-4o-mini` deployment when you use Chat.Api.

Set `EnableHybridSearch` back to `true` after you deploy `text-embedding-3-large`.

### `DeploymentNotFound` on search or chat

Hybrid search embeds the query via Azure OpenAI. `appsettings.json` expects these **deployment names** (not just model names):

| Config key | Default deployment name |
|------------|-------------------------|
| `AzureOpenAI:ChatDeployment` | `gpt-4o-mini` |
| `AzureOpenAI:EmbeddingDeployment` | `text-embedding-3-large` |

In [Azure AI Foundry](https://ai.azure.com) → your resource **YOUR-CHAT-RESOURCE** → **Deployments**:

1. Note the exact **Deployment name** for your embedding model.
2. Set `EmbeddingDeployment` in `appsettings.json` to that name (all services).
3. If you use `text-embedding-ada-002` or `text-embedding-3-small`, set `EmbeddingDimensions` to **1536** (not 3072).

Or create a new deployment: **Deploy model** → `text-embedding-3-large` → deployment name `text-embedding-3-large`.

**Automated (after `az login`):**

```powershell
az login
.\scripts\deploy-azure-openai-embeddings.ps1
```

**Manual (portal):**

1. Open [Azure AI Foundry](https://ai.azure.com) → resource **YOUR-CHAT-RESOURCE**
2. **Deployments** → **Deploy model** → **text-embedding-3-large**
3. Deployment name: `text-embedding-3-large` → **Deploy**
4. Restart Search.Api

### OpenTelemetry NU1902 warning

Upgrade `OpenTelemetry.Exporter.OpenTelemetryProtocol` to **1.15.3+** (already pinned in `NovaKnowledge.Infrastructure.csproj`).

## Documentation

- [Architecture Diagrams](docs/architecture/architecture-diagram.md)
- [Architecture ADRs](docs/architecture/)
- [Copilot Studio Setup](docs/copilot-studio-setup.md)
- [Interview Demo Script](docs/interview-demo-script.md)
- [Runbooks](docs/runbooks/)

## License

Portfolio / interview project — MIT
"# NovaKnowledge" 
