# NovaKnowledge — Architecture Diagrams

Production enterprise knowledge copilot for **Contoso Corp**. All external traffic enters through the YARP gateway; microservices share PostgreSQL, Redis, and Azure AI services.

---

## 1. System context

```mermaid
flowchart TB
    subgraph clients [Clients]
        AdminWeb["Blazor Admin Web"]
        Copilot["Copilot Studio"]
        RestClients["REST / Postman"]
    end

    subgraph platform [NovaKnowledge Platform]
        Gateway["YARP Gateway :5000"]
        Services[".NET 10 Microservices"]
        EvalPy["Python Evaluation :8000"]
    end

    subgraph azure [Azure Cloud]
        OpenAI["Azure OpenAI"]
        AISearch["Azure AI Search"]
        Foundry["Azure AI Foundry"]
        Blob["Blob Storage"]
        ServiceBus["Service Bus"]
        Postgres["PostgreSQL"]
        Redis["Redis Cache"]
        KeyVault["Key Vault"]
        AppInsights["App Insights"]
    end

    AdminWeb --> Gateway
    Copilot --> Gateway
    RestClients --> Gateway
    Gateway --> Services
    Services --> EvalPy
    Services --> OpenAI
    Services --> AISearch
    Services --> Foundry
    Services --> Blob
    Services --> ServiceBus
    Services --> Postgres
    Services --> Redis
    Services --> KeyVault
    Services --> AppInsights
```

---

## 2. Microservices topology

```mermaid
flowchart LR
    subgraph edge [Edge Layer]
        GW["Gateway\n:5000\nRate limit · Correlation ID"]
    end

    subgraph apis [API Layer]
        Chat["Chat.Api\n:5001\nSSE streaming"]
        Orch["Orchestration.Api\n:5002\nMulti-agent"]
        Search["Search.Api\n:5003\nHybrid RAG"]
        Ingest["Ingestion.Api\n:5004\nUpload"]
        Feedback["Feedback.Api\n:5005\nRatings"]
        Admin["Admin.Api\n:5006\nPrompts · Metrics"]
    end

    subgraph async [Async Layer]
        Worker["Ingestion.Worker\nService Bus consumer"]
    end

    subgraph ml [ML Layer]
        Eval["Evaluation Service\n:8000 FastAPI"]
    end

    GW --> Chat
    GW --> Orch
    GW --> Search
    GW --> Ingest
    GW --> Feedback
    GW --> Admin

    Chat --> Orch
    Chat --> Search
    Orch --> Search
    Ingest --> Worker
    Admin --> Eval
    Worker --> Search
```

| Service | Port | Responsibility |
|---------|------|----------------|
| Gateway | 5000 | YARP reverse proxy, rate limiting, `X-Correlation-Id` |
| Chat.Api | 5001 | Sessions, SSE chat, citation streaming |
| Orchestration.Api | 5002 | Router → Research → Synthesis → Compliance |
| Search.Api | 5003 | Hybrid vector + BM25 + semantic ranker |
| Ingestion.Api | 5004 | Multipart upload, dedup, queue publish |
| Feedback.Api | 5005 | Thumbs up/down, JSONL export |
| Admin.Api | 5006 | Prompts, tenant settings, eval trigger |
| Ingestion.Worker | — | Extract → chunk → embed → index |
| Evaluation Service | 8000 | RAG metrics, chunking benchmarks |

---

## 3. Shared libraries (Clean Architecture)

```mermaid
flowchart TB
    subgraph presentation [Presentation]
        GatewayLib["Gateway"]
        Apis["6 × Web API"]
        Blazor["Admin.Web"]
    end

    subgraph shared [Shared Libraries]
        Contracts["NovaKnowledge.Contracts\nDTOs · API schemas"]
        Application["NovaKnowledge.Application\nInterfaces · Chunking · Use cases"]
        Domain["NovaKnowledge.Domain\nEntities · Enums"]
        Infrastructure["NovaKnowledge.Infrastructure\nEF Core · Azure SDKs · Agents"]
    end

    GatewayLib --> Contracts
    Apis --> Infrastructure
    Apis --> Contracts
    Blazor --> Contracts
    Infrastructure --> Application
    Infrastructure --> Domain
    Infrastructure --> Contracts
    Application --> Domain
    Application --> Contracts
```

---

## 4. Document ingestion pipeline (RAG indexing)

```mermaid
sequenceDiagram
    participant User
    participant IngestApi as Ingestion.Api
    participant Blob as Blob Storage
    participant SB as Service Bus
    participant Worker as Ingestion.Worker
    participant Extract as Text Extraction
    participant Chunk as Chunking Service
    participant OpenAI as Azure OpenAI
    participant Index as Azure AI Search
    participant PG as PostgreSQL

    User->>IngestApi: POST /api/ingest/documents
    IngestApi->>Blob: Upload file
    IngestApi->>PG: Document status Pending
    IngestApi->>SB: DocumentUploaded event
    IngestApi-->>User: 202 Accepted

    SB->>Worker: Consume message
    Worker->>Blob: Download document
    Worker->>Extract: PDF / DOCX / TXT
    Worker->>Chunk: Recursive / Fixed / Semantic
    Worker->>OpenAI: Batch embeddings
    Worker->>Index: Index chunks + vectors
    Worker->>PG: Status Indexed
```

**Chunking strategies:** Recursive (default), Fixed (512/128 overlap), Semantic (benchmarked via Python eval service).

---

## 5. Chat and RAG query flow

```mermaid
sequenceDiagram
    participant User
    participant Chat as Chat.Api
    participant Redis as Redis Cache
    participant Search as Search.Api
    participant AISearch as Azure AI Search
    participant Orch as Orchestration.Api
    participant OpenAI as Azure OpenAI
    participant PG as PostgreSQL

    User->>Chat: POST /sessions/{id}/messages
    Chat->>Redis: Check retrieval cache

    alt Cache miss
        Chat->>Search: Hybrid query
        Search->>AISearch: Vector + BM25 + semantic ranker
        AISearch-->>Search: Top-k chunks
        Search-->>Chat: Citations + scores
        Chat->>Redis: Cache results
    end

    Chat->>Orch: Multi-agent orchestration
    Note over Orch,OpenAI: Router → Research → Synthesis → Compliance
    Orch->>OpenAI: LLM calls
    OpenAI-->>Orch: Grounded answer
    Orch-->>Chat: Answer + agent trace

    Chat->>PG: Persist message + tokens + latency
    Chat-->>User: SSE stream tokens + citations
```

---

## 6. Multi-agent orchestration

```mermaid
flowchart LR
    Input["User message"] --> Router["RouterAgent\nIntent classify"]
    Router -->|"policy_qa"| Research["ResearchAgent\nRetrieve chunks"]
    Router -->|"out_of_scope"| OOS["Decline gracefully"]
    Research --> Synthesis["SynthesisAgent\nCited answer"]
    Synthesis --> Compliance["ComplianceAgent\nDisclaimer · PII check"]
    Compliance --> Output["Final response"]
    Router --> Trace["AgentRun trace\nPostgreSQL"]
    Research --> Trace
    Synthesis --> Trace
    Compliance --> Trace
```

Toggle single-agent vs multi-agent via **Admin → Tenant Settings** (`UseMultiAgent`).

---

## 7. Azure deployment (Bicep)

```mermaid
flowchart TB
    subgraph rg [Resource Group]
        subgraph compute [Compute]
            ACA["Container Apps Environment"]
            ACR["Container Registry"]
            GWca["ca-gateway"]
            ChatCa["ca-chat-api"]
            OrchCa["ca-orchestration-api"]
            OtherCa["ca-ingest · search · feedback · admin · worker · eval"]
        end

        subgraph data [Data]
            PGflex["PostgreSQL Flexible"]
            RedisAz["Azure Cache for Redis"]
            Stor["Storage Account"]
            SBns["Service Bus"]
        end

        subgraph ai [AI Services]
            OAI["Azure OpenAI"]
            SRCH["AI Search"]
            AML["Azure ML Workspace"]
        end

        subgraph ops [Operations]
            KV["Key Vault"]
            LAW["Log Analytics"]
            APPi["Application Insights"]
        end
    end

    ACR --> ACA
    GWca --> ACA
    ChatCa --> ACA
    OrchCa --> ACA
    OtherCa --> ACA
    ACA --> PGflex
    ACA --> RedisAz
    ACA --> Stor
    ACA --> SBns
    ACA --> OAI
    ACA --> SRCH
    ACA --> KV
    APPi --> LAW
```

Deploy with:

```bash
az deployment sub create \
  --location eastus2 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

---

## 8. Observability and AI ops

```mermaid
flowchart LR
    subgraph services [All Services]
        OTel["OpenTelemetry\nTraces · Metrics"]
        Serilog["Serilog\nStructured logs"]
        Polly["Polly\nRetry · Circuit breaker"]
    end

    subgraph signals [Signals]
        Tokens["tokens_used"]
        Latency["retrieval_latency_ms\nllm_latency_ms"]
        Cost["estimated_cost_usd"]
        Feedback["feedback export → eval dataset"]
    end

    subgraph dest [Destinations]
        AppIns["Application Insights"]
        Dashboard["Admin metrics dashboard"]
        EvalJob["Python RAG evaluation"]
    end

    services --> OTel
    services --> Serilog
    OTel --> AppIns
    OTel --> signals
    signals --> Dashboard
    Feedback --> EvalJob
```

---

## 9. Copilot Studio integration

```mermaid
flowchart LR
    M365["Microsoft 365 User"] --> CS["Copilot Studio"]
    CS --> Connector["Custom Connector"]
    Connector --> GW["NovaKnowledge Gateway"]
    GW --> Chat["Chat.Api"]
    GW --> Search["Search.Api"]
```

> In this demo the connector calls the Gateway without authentication. Adding Entra ID
> OAuth is a documented production extension — see [Copilot Studio Setup](../copilot-studio-setup.md).

See [Copilot Studio Setup](../copilot-studio-setup.md) for connector configuration.

---

## Related docs

- [ADR-001: Hybrid Search](ADR-001-hybrid-search.md)
- [ADR-002: Multi-Agent](ADR-002-multi-agent.md)
- [ADR-003: Chunking](ADR-003-chunking.md)
- [Interview Demo Script](../interview-demo-script.md)
- [Incident Response Runbook](../runbooks/incident-response.md)
