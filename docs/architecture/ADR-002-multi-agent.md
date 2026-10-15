# ADR-002: Multi-Agent Pipeline vs Single-Prompt RAG

- **Status:** Accepted
- **Date:** 2026-01
- **Deciders:** NovaKnowledge maintainers

## Context

Enterprise copilots must do more than retrieve-and-generate: they must decide whether a question is
even in scope, ground the answer in retrieved context, and apply compliance guardrails. A single
prompt conflates these concerns, which makes failures hard to diagnose and citations hard to trust.

## Decision

Implement a **sequential multi-agent pipeline** on **Microsoft Semantic Kernel**:

1. **RouterAgent** (`ChatCompletionAgent`) — classifies intent as `policy_qa`, `summarization`, or
   `out_of_scope`; out-of-scope questions short-circuit.
2. **ResearchAgent** — a **deterministic** step that formats retrieved chunks into numbered context.
   It is intentionally *not* an LLM call: retrieval already happened, so spending a model call to
   re-format it adds cost without quality.
3. **SynthesisAgent** (`ChatCompletionAgent`) — produces a grounded, cited answer constrained to the
   provided context.
4. **ComplianceAgent** (`ChatCompletionAgent`) — revises for invented policy details and appends a
   verification note.

The pipeline can be toggled at the tenant level (`TenantSettings.UseMultiAgent`) to A/B against a
single-prompt path. Every step records duration and token usage into `AgentRun` trace rows linked to
the assistant message.

## Alternatives considered

| Option | Why not |
|--------|---------|
| Single large prompt | Cheap and low latency, but no per-step observability and weaker guardrails. Retained as the toggle-off path. |
| Fully autonomous agent loop | Unbounded cost/latency and non-deterministic step counts; poor fit for a compliance-sensitive corpus. |
| Framework-specific orchestration only | Couples the design to one framework; using SK primitives keeps agents portable and testable. |

## Consequences

- **Cost/latency:** up to ~3 model calls per query (router + synthesis + compliance) versus 1 for
  single-prompt; measured as `TotalLlmLatencyMs` in the trace and admin metrics.
- **Observability:** the full trace is persisted and exposed via `GET /api/chat/sessions/{id}/trace?messageId=`,
  so token/latency regressions are attributable to a specific agent.
- **Resilience:** keeping Research deterministic removes one failure mode and one model dependency
  from the critical path.
- **Demo scope:** agent names here match the running code; the earlier "ResearchAgent calls the LLM"
  description was inaccurate and has been corrected.
