# Copilot Studio Integration Guide

> **Scope note:** NovaKnowledge ships as a **single-tenant demo with no authentication**.
> The OAuth/Entra ID steps below describe a **production extension**, not functionality
> that exists in this repository. In the current build the Gateway exposes the APIs
> directly (see the "Scope & Limitations" section of the [README](../README.md)).

## Overview

NovaKnowledge exposes REST APIs via the YARP Gateway for Microsoft Copilot Studio custom connectors.

## Prerequisites

- Power Platform environment with Copilot Studio license
- NovaKnowledge Gateway deployed and accessible (HTTPS)

## Step 1: Create Custom Connector

1. Power Platform → Custom Connectors → New
2. General:
   - Host: `your-gateway.azurecontainerapps.io`
   - Base URL: `/`
3. Actions:

### AskQuestion
- Method: POST
- URL: `/api/chat/sessions/{sessionId}/messages`
- Body:
  ```json
  { "content": "{question}" }
  ```

### SearchKnowledge
- Method: POST
- URL: `/api/search/query`
- Body:
  ```json
  { "query": "{query}", "tenantId": "contoso", "topK": 5 }
  ```

### CreateSession
- Method: POST
- URL: `/api/chat/sessions`
- Body: `{ "tenantId": "contoso" }`

## Step 2: Build Copilot Topics

**Topic: Ask HR Policy**
1. Trigger phrase: "What is the PTO policy?"
2. Call `CreateSession` → store session ID
3. Call `AskQuestion` with user question
4. Display response

**Topic: Search Documents**
1. Call `SearchKnowledge` directly for quick lookups

## Architecture Note

Copilot Studio handles M365 conversational UX; NovaKnowledge handles RAG, governance, token tracking, and agent orchestration in .NET microservices.

## Production Extension: Entra ID OAuth (Not Implemented)

To harden this for production you would add authentication in front of the Gateway. This is
**design guidance only** — no auth code is present in this repo:

1. Azure Portal → Entra ID → App registrations → New registration
2. Name: `NovaKnowledge-Copilot-Connector`
3. Redirect URI: `https://global.consent.azure-apim.net/redirect`
4. Expose an API scope: `api://{client-id}/access_as_user`
5. Add JWT Bearer validation to the Gateway using the Entra ID metadata document
6. Configure the custom connector security as OAuth 2.0 (Authorization Code)
