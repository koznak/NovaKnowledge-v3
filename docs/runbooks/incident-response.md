# Incident Response Runbook

## High Error Rate on Chat API

1. Check `/health` on all services via Gateway
2. Review App Insights for 429 errors from Azure OpenAI
3. If quota exceeded: switch tenant to `gpt-4o-mini` via Admin settings
4. Polly circuit breaker opens after 5 failures — wait 30s for recovery

## Ingestion Stuck in Processing

1. Check Service Bus dead-letter queue for `document-uploaded`
2. Review Ingestion.Worker logs for extraction failures
3. Re-trigger: `POST /api/admin/documents/{id}/reindex`

## Search Returns No Results

1. Verify index exists: `POST /api/search/index/ensure`
2. Check document status is `Indexed`
3. Verify Azure Search API key and endpoint in Key Vault

## Daily Token Budget Exceeded

1. Admin metrics: `GET /api/admin/metrics/usage`
2. Enable Redis retrieval cache (reduces repeat embedding calls)
3. Reduce `RetrievalTopK` in tenant settings
