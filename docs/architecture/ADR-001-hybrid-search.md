# ADR-001: Hybrid Search over Vector-Only Retrieval

## Status
Accepted

## Context
Contoso Corp documents contain acronyms (PTO, BYOD, MDM) that pure vector search sometimes misses.

## Decision
Use Azure AI Search hybrid search (BM25 + vector k-NN) with semantic ranker enabled.

## Consequences
- Requires Search Standard tier for production semantic ranker
- Better recall on acronym-heavy HR/IT docs
- Slightly higher latency vs vector-only (~50-100ms)
