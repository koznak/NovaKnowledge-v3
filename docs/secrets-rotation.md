# Secret History Scrub & Key Rotation

This document is the one-time procedure to make the repository safe to go public. Earlier commits
contained **live Azure credentials** and **personal information**, so they must be removed from
**all history** (not just the current files) and the keys must then be **rotated** in Azure.

> **Rotate first, then scrub.** Assume the keys are already compromised, so regenerating them first
> is both safer and simpler than ordering the steps the other way around.

> **Do not put real secret values in this file.** It is committed. The actual replacement strings
> live in a local, git-ignored file (`secrets.txt`).

> These steps rewrite git history and are destructive. Work on a backup/fresh clone. See
> "Run it yourself" at the bottom.

## 0. What is actually exposed

| Kind | Value (historical) | Appears in |
|------|--------------------|------------|
| Azure OpenAI chat key | `REDACTED_AZURE_OPENAI_CHAT_KEY` | 8 × `appsettings.json`, `appsettings.shared.json` |
| Azure OpenAI embedding key | `REDACTED_AZURE_OPENAI_EMBEDDING_KEY` | same |
| Azure AI Search admin key | `REDACTED_AZURE_SEARCH_KEY` | same |
| Resource names / endpoints | `YOUR-CHAT-RESOURCE`, `YOUR-SEARCH-RESOURCE`, resource group | appsettings + scripts |
| Personal info | name, email, employer, `docs/redacted-company-interview-brief.{md,pdf}` | `first commit` + prior |

The placeholders above are the *target* replacements; the real values are only ever in `secrets.txt`
and Azure.

## 1. Rotate the keys in Azure (do this first)

The three keys were live, so regenerate them now:

1. Azure OpenAI (chat resource) → Keys and Endpoint → **Regenerate Key 1**.
2. Azure OpenAI (embedding resource) → Keys and Endpoint → **Regenerate Key 1**.
3. Azure AI Search → Keys → **Regenerate** the admin key.

Store the new values only via `dotnet user-secrets` (the repo shares one store,
`novaknowledge-local-development`) or an ignored `appsettings.*.local.json`. Never commit them.

## 2. Build the replacement map (local, git-ignored)

`secrets.txt` in the repo root is already listed in `.gitignore`, so it can never be committed.
One `literal:` rule per line; `git-filter-repo` replaces the exact strings across every commit:

```
literal:<actual-chat-key>==>REDACTED_AZURE_OPENAI_CHAT_KEY
literal:<actual-embedding-key>==>REDACTED_AZURE_OPENAI_EMBEDDING_KEY
literal:<actual-search-admin-key>==>REDACTED_AZURE_SEARCH_KEY
literal:<actual-resource-group>==>YOUR-RESOURCE-GROUP
literal:<actual-resource-name>==>YOUR-RESOURCE
literal:<actual-chat-resource>==>YOUR-CHAT-RESOURCE
literal:<actual-search-resource>==>YOUR-SEARCH-RESOURCE
literal:<actual-email>==>REDACTED_EMAIL
literal:<actual-name>==>REDACTED_NAME
literal:<actual-company>==>REDACTED_COMPANY
```

> Order matters only in that longer strings should come first (e.g. the full email before the
> name part), because `replace-text` applies rules in file order.

## 3. Rewrite history

```bash
pip install git-filter-repo
git filter-repo --replace-text secrets.txt
```

This removes the personal brief files from history too (they only ever contained the redacted
strings, so after replacement they are gone). `git-filter-repo` intentionally **removes the
`origin` remote** as a safety measure.

## 4. Verify before pushing

```bash
# Keys: every one of these must print nothing
git log --all -p | rg "<actual-chat-key-fragment>"
git grep -l "<actual-key>" $(git rev-list --all)

# PII: must print nothing
git log --all -p | rg -i "<name>|<email>|<company>"
git log --all --name-only --pretty=format: | rg -i "interview-brief"
```

## 5. Re-add the remote and force-push

```bash
git remote add origin https://github.com/koznak/NovaKnowledge.git
git push --force --all origin
git push --force --tags origin
```

## 6. Confirm

- [ ] Keys rotated in Azure; new values only in user-secrets / ignored files.
- [ ] Every `rg` check in step 4 prints nothing.
- [ ] The public repo's history (browse a commit from before the scrub) shows no secrets/PII.
- [ ] The CI gitleaks job passes.

## Run it yourself

I (the assistant) do **not** run history rewrites or force-pushes. The steps are destructive and
must be run by you, ideally on a fresh clone:

```bash
git clone https://github.com/koznak/NovaKnowledge.git nk-scrub && cd nk-scrub
# add secrets.txt, then follow steps 2–6
```
