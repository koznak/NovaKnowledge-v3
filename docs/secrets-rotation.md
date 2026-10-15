# Secret History Scrub & Key Rotation

This document covers the one-time steps required before making the repository public.
Earlier commits contained **live Azure credentials**, so they must be removed from **all
history** and then **rotated** in Azure.

> **Do not put the real secret values in this file.** It is committed. Keep the mapping in a
> local, git-ignored file (`secrets.txt`) and record only the *locations* below.

> These steps rewrite git history and are destructive. Run them on a fresh clone or
> after backing up. Do **not** run them from an agent session — run them yourself.

## 1. Inventory the exposed credentials

The following committed files previously contained live values (they now contain placeholders):

- `src/Services/{Chat,Search,Ingestion,Feedback,Admin,Orchestration}/**/appsettings.json`
- `src/Services/Ingestion/NovaKnowledge.Ingestion.Worker/appsettings.json`
- `src/Shared/NovaKnowledge.Infrastructure/appsettings.shared.json`
- `scripts/deploy-azure-openai-embeddings.ps1` (hardcoded resource name)

Collect the actual values from your git history (for example
`git log --all -p | rg "ApiKey"`) and the Azure portal. Treat every one as compromised.

## 2. Build the replacement map (local, ignored)

Create `secrets.txt` in the repository root (it is git-ignored). One rule per line, using
`git-filter-repo`'s `literal:` form so the exact strings are replaced without touching the rest
of the history:

```
literal:<actual-chat-key>==>REDACTED_AZURE_OPENAI_CHAT_KEY
literal:<actual-embedding-key>==>REDACTED_AZURE_OPENAI_EMBEDDING_KEY
literal:<actual-search-admin-key>==>REDACTED_AZURE_SEARCH_KEY
literal:<actual-chat-resource-name>==>YOUR-CHAT-RESOURCE
literal:<actual-embedding-resource-name>==>YOUR-EMBEDDING-RESOURCE
literal:<actual-search-resource-name>==>YOUR-SEARCH-RESOURCE
```

## 3. Rewrite history

```bash
pip install git-filter-repo
git filter-repo --replace-text secrets.txt
```

Verify the values are gone from every commit (substitute one of the real strings):

```bash
git log --all -p | rg "<actual-key-fragment>"   # should print nothing
```

## 4. Force-push the rewritten history

```bash
git remote add origin <your-repo-url>   # filter-repo removes remotes
git push --force --all origin
git push --force --tags origin
```

## 5. Rotate the exposed credentials

The keys were live, so regenerate them in Azure **after** the scrub:

1. Azure OpenAI (chat resource) -> Keys and Endpoint -> **Regenerate Key 1**.
2. Azure OpenAI (embedding resource) -> Keys and Endpoint -> **Regenerate Key 1**.
3. Azure AI Search -> Keys -> **Regenerate** the admin key.

Then store the new values only in `dotnet user-secrets` (or an ignored
`appsettings.*.local.json`). Never commit them.

## 6. Confirm

- [ ] `git log --all -p | rg "<each old value>"` returns nothing.
- [ ] The public repo's history no longer contains the values.
- [ ] New keys are stored only in user-secrets / ignored local files.
- [ ] The CI gitleaks job passes.
