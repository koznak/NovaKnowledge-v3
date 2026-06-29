# Copilot Studio Integration Guide

## Overview

NovaKnowledge exposes REST APIs via the YARP Gateway for Microsoft Copilot Studio custom connectors.

## Prerequisites

- Power Platform environment with Copilot Studio license
- Azure Entra ID app registration for OAuth
- NovaKnowledge Gateway deployed and accessible (HTTPS)

## Step 1: Register Entra ID Application

1. Azure Portal → Entra ID → App registrations → New registration
2. Name: `NovaKnowledge-Copilot-Connector`
3. Redirect URI: `https://global.consent.azure-apim.net/redirect`
4. Expose an API scope: `api://{client-id}/access_as_user`
5. Note Client ID and Tenant ID

## Step 2: Configure Gateway OAuth (Production)

Add JWT Bearer validation to Gateway using Entra ID metadata.
For dev, use API key header `X-Api-Key`.

## Step 3: Create Custom Connector

1. Power Platform → Custom Connectors → New
2. General:
   - Host: `your-gateway.azurecontainerapps.io`
   - Base URL: `/`
3. Security: OAuth 2.0 (Authorization Code) with Entra ID
4. Actions:

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

## Step 4: Build Copilot Topics

**Topic: Ask HR Policy**
1. Trigger phrase: "What is the PTO policy?"
2. Call `CreateSession` → store session ID
3. Call `AskQuestion` with user question
4. Display response

**Topic: Search Documents**
1. Call `SearchKnowledge` directly for quick lookups

## Architecture Note

Copilot Studio handles M365 conversational UX; NovaKnowledge handles RAG, governance, token tracking, and agent orchestration in .NET microservices.
