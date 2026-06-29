# ADR-002: Multi-Agent vs Single-Prompt RAG

## Status
Accepted

## Context
Enterprise copilots need intent routing, citation grounding, and compliance guardrails.

## Decision
Implement sequential multi-agent pipeline: Router → Research → Synthesis → Compliance.
Toggle via `TenantSettings.UseMultiAgent` for A/B comparison.

## Consequences
- ~3x LLM calls per query (higher cost, higher quality)
- Full agent trace persisted for observability and interview demos
- Compliance agent adds disclaimer without blocking latency-critical path
