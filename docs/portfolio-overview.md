# NovaKnowledge — Portfolio Overview

A sanitized summary of what this repository is and what it demonstrates. No employer, personal, or
cloud-resource details are included.

## One-liner

A grounding-first **enterprise RAG knowledge copilot** built on .NET 10 microservices, Azure OpenAI,
Azure AI Search, and Microsoft Semantic Kernel, with a Python service that computes real Ragas
evaluation metrics.

## The problem it models

An organization has HR, IT, and policy documents scattered across formats. Employees ask natural
questions ("What is the PTO policy?") and need answers that are **grounded, cited, and compliant** —
not plausible-sounding text. The interesting engineering is therefore in retrieval quality,
citations, guardrails, and measurable evaluation, not in the prompt.

## What was built

- **Microservices** (6 APIs + YARP gateway + ingestion worker + Blazor admin) with clean layering:
  `Domain` / `Contracts` / `Application` / `Infrastructure` / services.
- **Ingestion pipeline**: upload → content-hash dedup → Service Bus event → worker extracts text →
  chunks → embeds → indexes; failures transition the document to `Failed` with an error message.
- **Hybrid retrieval**: Azure AI Search vector + BM25 + semantic ranker, with tenant/department/
  docType filters and a deterministic (SHA-256) cache key.
- **Grounded chat**: SSE token streaming with `citations → token* → done` framing, citation DTOs,
  and Prometheus-style metrics per turn.
- **Agent orchestration**: Microsoft Semantic Kernel `ChatCompletionAgent` pipeline
  (Router → Research → Synthesis → Compliance) with per-step token/latency traces linked to messages.
- **Evaluation**: FastAPI service running Ragas (LLM judge) against a golden Q&A set, with an
  explicitly labeled deterministic fallback for offline/CI runs.
- **Operational concerns**: RFC 7807 error handling, liveness vs readiness probes, Polly retry +
  circuit breaker, Serilog, OpenTelemetry, k6 load tests, Bicep IaC, GitHub Actions CI.

## Engineering decisions worth discussing

| Decision | Trade-off captured |
|----------|--------------------|
| Hybrid over vector-only ([ADR-001](architecture/ADR-001-hybrid-search.md)) | Better acronym recall vs Standard-tier cost + 50–100 ms latency |
| Multi-agent over single prompt ([ADR-002](architecture/ADR-002-multi-agent.md)) | Up to 3× model calls vs per-step observability and guardrails |
| Recursive chunking default ([ADR-003](architecture/ADR-003-chunking.md)) | Paragraph fidelity vs variable chunk length |
| Real Ragas with labeled fallback | Honest metrics vs dependency on LLM credentials |

## Honest limitations

NovaKnowledge is a **single-tenant demonstration with no authentication**. `tenantId` is a
configuration/request default, not an authenticated identity. The cost model is approximate. Entra ID
OAuth, multi-tenant isolation, and production hardening are documented as extensions, not implemented.

## How to review it

1. Read the [README](../README.md) "What this demonstrates" and "Scope & Limitations" sections.
2. Skim the [architecture diagrams](architecture/architecture-diagram.md) and ADRs.
3. Look at `src/Shared/NovaKnowledge.Infrastructure/Agents/AgentOrchestrator.cs` and
   `src/Services/Chat/NovaKnowledge.Chat.Api/Controllers/ChatController.cs` for the core RAG loop.
4. Run `dotnet test NovaKnowledge.sln` and `pytest python/evaluation-service -q`.
