# ADR-001: Hybrid Search over Vector-Only Retrieval

- **Status:** Accepted
- **Date:** 2026-01
- **Deciders:** NovaKnowledge maintainers

## Context

Enterprise HR/IT knowledge bases are dense with acronyms (PTO, BYOD, MDM, SSO) and exact
identifiers (policy codes, ticket prefixes). Pure dense-vector retrieval embeds these into
approximate positions and frequently ranks a semantically similar but non-matching chunk above the
precise one. Conversely, pure keyword (BM25) retrieval misses paraphrases and synonyms.

The retrieval corpus here is a small, high-precision policy corpus where a wrong citation is worse
than no answer, so recall on exact terms matters as much as semantic recall.

## Decision

Use **Azure AI Search hybrid search**: BM25 keyword matching and vector k-NN over the same
`contentVector` field, fused by the search engine, then re-ranked by the **semantic ranker** with an
extractive query caption. Hybrid is the intended production mode (the code default is
`EnableHybridSearch: true`); the shipped demo `appsettings.json` sets it to `false` (keyword-only) so
the stack runs without an embedding deployment — see the README "Running without Azure OpenAI".

## Alternatives considered

| Option | Why not |
|--------|---------|
| Vector-only | Misses exact acronym/identifier matches; no lexical signal to fuse. |
| Keyword-only (BM25) | Cheap and Azure-embedding-free, but weak on paraphrased questions. Kept as a supported mode, not the default. |
| External re-ranker | Extra service + latency + cost for a corpus size where the built-in semantic ranker is sufficient. |

## Consequences

- **Quality:** better recall on acronym-heavy queries; matches the "hybrid beats vector-only on PTO"
  result cited in the demo script.
- **Cost/tier:** the semantic ranker requires Azure AI Search **Standard** tier or higher; Basic does
  not support it.
- **Latency:** semantic ranking adds roughly 50–100 ms per query on top of vector + keyword fusion.
- **Operational:** requires a valid `EmbeddingDeployment` and matching `VectorDimensions` (3072 for
  `text-embedding-3-large`, 1536 for `3-small`/`ada-002`); a mismatch fails indexing. The index
  dimension is driven from configuration so a single value controls both indexing and the index schema.
