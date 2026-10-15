# ADR-003: Recursive Chunking as Default

- **Status:** Accepted
- **Date:** 2026-01
- **Deciders:** NovaKnowledge maintainers

## Context

Retrieval quality is heavily determined by chunking. Policy documents are structured as headings and
paragraphs; fixed-size windows cut across those boundaries and produce chunks that mix unrelated
topics, which lowers answer relevance and makes citations confusing.

Three strategies were evaluated: recursive (paragraph/sentence-aware), fixed (uniform size/overlap),
and semantic (embedding-similarity boundaries), benchmarked by the Python evaluation service.

## Decision

Default to **recursive chunking** (target 512 tokens, 128 overlap), driven by tenant settings
(`DefaultChunkingStrategy`, `FixedChunkSize`, `FixedChunkOverlap`). Expose the strategy per upload and
per tenant. Keep **fixed** available for uniform token windows (e.g. downstream tooling that expects
constant sizes). **Semantic** chunking is benchmarked in the evaluation service but is not on the
production ingestion hot path.

## Alternatives considered

| Option | Why not (as default) |
|--------|----------------------|
| Fixed-size | Boundaries cut across paragraphs; lower precision. Available, not default. |
| Semantic | Higher compute cost and an extra embedding pass per chunk; not justified as a default for a paragraph-structured corpus. Benchmarked only. |

## Consequences

- **Quality:** recursive preserves paragraph boundaries, so a cited chunk answers one question.
- **Operational:** chunk size/overlap are configurable per tenant without redeploying.
- **Benchmarking:** the evaluation service's `/evaluate/chunking` endpoint compares strategies on
  real text so the default can be revisited with data rather than intuition.
- **Trade-off:** recursive chunks vary in length, so downstream pipelines must not assume a fixed
  token count.
