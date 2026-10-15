# Secret History Scrub & Key Rotation

This document covers the one-time steps required before making the repository public.
The committed keys were live Azure credentials, so they must be removed from **all
history** and then **rotated** in Azure.

> These steps rewrite git history and are destructive. Run them on a fresh clone or
> after backing up. Do **not** run them from an agent session — run them yourself.

## 1. Build the replacement map

Create `secrets.txt` in the repository root (it is git-ignored). One rule per line, in
the form `literal:<real-value>==><replacement>` so `git-filter-repo` replaces the exact
strings without touching the rest of the history:

```
REDACTED_AZURE_OPENAI_CHAT_KEY==>REDACTED_AZURE_OPENAI_CHAT_KEY
REDACTED_AZURE_OPENAI_EMBEDDING_KEY==>REDACTED_AZURE_OPENAI_EMBEDDING_KEY
REDACTED_AZURE_SEARCH_KEY==>REDACTED_AZURE_SEARCH_KEY
YOUR-RESOURCE-GROUP==>YOUR-CHAT-RESOURCE
YOUR-CHAT-RESOURCE==>YOUR-EMBEDDING-RESOURCE
YOUR-SEARCH-RESOURCE==>YOUR-SEARCH-RESOURCE
```

`git-filter-repo` expects the `literal:` prefix form:

```
literal:REDACTED_AZURE_OPENAI_CHAT_KEY==>REDACTED_AZURE_OPENAI_CHAT_KEY
```

## 2. Rewrite history

```bash
pip install git-filter-repo
git filter-repo --replace-text secrets.txt
```

Verify the keys are gone from every commit:

```bash
git log --all -p | rg "REDACTED_KEY|REDACTED_KEY|REDACTED_KEY"   # should print nothing
```

## 3. Force-push the rewritten history

```bash
git remote add origin https://github.com/koznak/NovaKnowledge.git   # filter-repo removes remotes
git push --force --all origin
git push --force --tags origin
```

## 4. Rotate the exposed credentials

The keys above were live, so regenerate them in Azure **after** the scrub:

1. Azure OpenAI (chat resource) -> Keys and Endpoint -> **Regenerate Key 1**.
2. Azure OpenAI (embedding resource) -> Keys and Endpoint -> **Regenerate Key 1**.
3. Azure AI Search -> Keys -> **Regenerate** the admin key.

Then update your local `dotnet user-secrets` (or `appsettings.*.local.json`) with the new
values. Do not commit them.

## 5. Confirm

- [ ] `git log --all -p | rg "<each old key>"` returns nothing.
- [ ] The public repo's blame/history no longer contains the keys.
- [ ] New keys are stored only in user-secrets / ignored local files.
- [ ] CI gitleaks job passes.
