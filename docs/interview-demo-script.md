# NovaKnowledge — 15-Minute Interview Demo Script

## Setup (before interview)
- [ ] Gateway + all services running (or Azure Container Apps deployed)
- [ ] At least 2 Contoso HR/IT PDFs indexed
- [ ] Admin UI open at Dashboard
- [ ] Postman/curl ready for API calls
- [ ] App Insights dashboard open (optional)

## Act 1: Platform Overview (2 min)
"We built NovaKnowledge — an enterprise knowledge copilot for Contoso Corp using .NET 10 microservices on Azure."

Show architecture diagram in README:
- 6 microservices behind YARP Gateway
- Event-driven ingestion via Service Bus
- PostgreSQL for conversations, Azure AI Search for retrieval

## Act 2: Document Ingestion (3 min)
1. Open Admin UI → Documents
2. Upload a PDF (HR policy)
3. Show status transition: Pending → Processing → Indexed
4. Explain: "Worker extracts text, chunks recursively, embeds with text-embedding-3-large, indexes to Azure AI Search with hybrid vectors."

**Talking point:** Idempotent ingestion via content hash dedup.

## Act 3: RAG Chat with Citations (4 min)
```bash
# Create session
curl -X POST http://localhost:5000/api/chat/sessions \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"contoso"}'

# Send message (SSE stream)
curl -N -X POST http://localhost:5000/api/chat/sessions/{id}/messages \
  -H "Content-Type: application/json" \
  -d '{"content":"What is the PTO policy?"}'
```

Show:
- Citations in response `[1]`, `[2]`
- SSE streaming tokens
- Agent trace: `GET /api/chat/sessions/{id}/trace`

**Talking point:** Hybrid search beat vector-only on acronyms like PTO.

## Act 4: Multi-Agent Orchestration (3 min)
Explain agent chain:
1. **RouterAgent** — classifies intent
2. **ResearchAgent** — retrieves chunks
3. **SynthesisAgent** — generates cited answer
4. **ComplianceAgent** — adds disclaimer

Call orchestration directly:
```bash
curl -X POST http://localhost:5000/api/orchestration/run \
  -H "Content-Type: application/json" \
  -d '{"message":"What is BYOD policy?","tenantId":"contoso","conversationId":"..."}'
```

## Act 5: AI Ops & Evaluation (3 min)
1. Admin Dashboard → show token usage, latency metrics
2. Prompts page → Trigger RAG Evaluation
3. Explain Python evaluation service computes faithfulness/relevance scores
4. Mention Polly retry on 429, Redis cache for retrieval, k6 load test results

## Closing Questions to Invite
- "Want to see the Bicep IaC or CI/CD pipeline?"
- "I can walk through the Copilot Studio custom connector setup."
- "Happy to discuss trade-offs on chunking strategy — we A/B tested with the Python eval pipeline."

## Interview Talking Points Cheat Sheet

| JD Topic | Your Story |
|----------|-----------|
| RAG | Hybrid search + recursive chunking + embedding cache |
| Multi-agent | 4-agent pipeline with persisted traces |
| Azure OpenAI | Centralized prompts, token cost tracking, model toggle |
| Azure AI Search | Vector + BM25 + semantic ranker |
| Azure ML lifecycle | Python eval pipeline, golden Q&A dataset, weekly benchmarks |
| Copilot Studio | Custom connector → Gateway APIs |
| Production | Bicep IaC, Container Apps, Key Vault, OTel, DLQ, Polly |
