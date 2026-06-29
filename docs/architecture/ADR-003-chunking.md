# ADR-003: Recursive Chunking as Default

## Status
Accepted

## Context
Three chunking strategies evaluated: recursive, fixed (512/128), semantic (Python benchmark).

## Decision
Default to recursive chunking; expose strategy per upload and tenant settings.
Python evaluation service runs offline chunking A/B tests.

## Consequences
- Recursive preserves paragraph boundaries for policy documents
- Fixed available for uniform token windows
- Semantic chunking delegated to evaluation pipeline, not production v1 hot path
