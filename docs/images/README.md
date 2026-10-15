# Images & visuals

This folder holds the visual assets embedded in the top-level `README.md`.

| File | Purpose |
|------|---------|
| `architecture.svg` | Illustrated system architecture (rendered inline in the README). |
| `admin-dashboard.png` | *(to add)* Screenshot of the Admin UI dashboard: token usage, latency, cost. |
| `admin-documents.png` | *(to add)* Screenshot of the document list showing status transitions. |
| `chat-citations.png` | *(to add)* Screenshot of a cited SSE chat answer with `[1]`, `[2]` citations. |
| `demo.gif` | *(to add)* Short recording: ingest → search → cited streaming chat. |

## Adding screenshots

The screenshots are intentionally not committed yet because they require a running
stack with Azure OpenAI + Azure AI Search configured. To generate them:

1. `docker compose up -d postgres redis azurite servicebus-sql servicebus-emulator`
2. Configure secrets via `dotnet user-secrets` (see the README Quick Start).
3. Run the services and `src/Web/NovaKnowledge.Admin.Web`.
4. Capture the screens above at ~1440px width, save them here with the names above,
   and remove the "*(to add)*" markers in this table.
