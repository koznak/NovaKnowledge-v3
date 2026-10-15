# Architecture documentation

Index of the architecture documents. Diagrams and decisions are kept in sync with the code; where
the code and a document disagree, the code wins and the document is a bug.

| Document | Contents |
|----------|----------|
| [architecture-diagram.md](architecture-diagram.md) | System, microservices, RAG, chat, multi-agent, deployment, and observability diagrams (Mermaid). |
| [ADR-001-hybrid-search.md](ADR-001-hybrid-search.md) | Why hybrid (vector + BM25 + semantic ranker) is the retrieval default. |
| [ADR-002-multi-agent.md](ADR-002-multi-agent.md) | Why a Semantic Kernel agent pipeline, and where each agent's responsibility ends. |
| [ADR-003-chunking.md](ADR-003-chunking.md) | Why recursive chunking is the default and how the others are benchmarked. |

Related:

- [Portfolio overview](../portfolio-overview.md)
- [Copilot Studio setup](../copilot-studio-setup.md)
- [Secret rotation & history scrub](../secrets-rotation.md)
- [Incident response runbook](../runbooks/incident-response.md)
