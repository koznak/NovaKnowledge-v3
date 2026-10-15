# Secret History Scrub & Key Rotation

This document is the one-time procedure to make the repository safe to go public. Earlier commits
contained **live Azure credentials** and **personal information**, so they must be removed from
**all history** (not just the current files) and the keys must be **rotated** in Azure.

> **Rotating the keys is the security step; scrubbing history is hygiene.** Once a key is rotated the
> copies in history are worthless, so rotation is mandatory regardless of what you do with git.

> **Recommended approach — new repo (Option C).** Rather than force-pushing the existing repo, push
> the scrubbed history to a **brand-new repository**. The secrets were never sent to the new remote,
> so there are no dangling/cached objects, and no force-push is required.

> **Do not put real secret values in this file.** It is committed. The actual replacement strings
> live in a local, git-ignored file (`secrets.txt`).

> These steps rewrite git history and are destructive. Work on a copy. See "Run it yourself" at the
> bottom.

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

> **Include truncated forms too.** Any place that showed a key *prefix* (for example a shortened
> fragment in old documentation or a verification command) must have its own rule, otherwise the
> fragment survives the scrub. `scripts/scrub-history.ps1` verifies every literal you list, so add
> fragments until the verification passes.

## 3. Rewrite history

The repository ships `scripts/scrub-history.ps1`, which automates steps 3–5 safely (it works on a
throwaway clone and never touches your existing working tree or remote):

```powershell
.\scripts\scrub-history.ps1
```

It clones to a temp folder, removes the personal brief files, runs `git filter-repo --replace-text`,
and then **fails loudly** if any scrubbed literal survives in any commit. On success it prints the
exact publish steps.

Equivalent manual steps:

```bash
pip install git-filter-repo
git filter-repo --path docs/redacted-company-interview-brief.md --path docs/redacted-company-interview-brief.pdf --invert-paths
git filter-repo --replace-text secrets.txt
```

> `--replace-text` cannot alter binary files (the PDF) or file names, which is why the personal brief
> files are removed with `--invert-paths` first. The script does both passes.

`git-filter-repo` intentionally **removes the `origin` remote** as a safety measure.

## 4. Verify before pushing

```bash
# Keys: every one of these must print nothing
git log --all -p | rg "<actual-chat-key-fragment>"
git grep -l "<actual-key>" $(git rev-list --all)

# PII: must print nothing
git log --all -p | rg -i "<name>|<email>|<company>"
git log --all --name-only --pretty=format: | rg -i "interview-brief"
```

## 5. Publish the cleaned history to a NEW repo (recommended)

Pushing the scrubbed clone to a **brand-new repository** is safer than force-pushing the old one:
the secrets were never sent to the new remote, so there are no dangling/cached objects to worry
about, and no force-push is needed.

```powershell
.\scripts\scrub-history.ps1 -NewRepo <owner>/<new-repo> -EmailMap email-map.txt
```

`-EmailMap` rewrites the commit **author/committer email** (which lives in commit metadata, not file
content, so `--replace-text` cannot touch it). Create `email-map.txt` (git-ignored) in the standard
`.mailmap` format:

```
Your Name <123456+you@users.noreply.github.com> <you@personal-email.com>
```

Find your noreply address at **GitHub → Settings → Emails → Keep my email addresses private**. The
script verifies that no old email survives in any author, committer, or diff after the rewrite.

This creates a new **private** repo and pushes the cleaned history to it. Push it public directly
with `-Visibility public` (only do this once you're confident the scrub is complete). Or do it
manually:

```bash
cd <scrubbed-clone-dir>
gh repo create <owner>/<new-repo> --private --source . --remote origin
git push -u origin main
```

Then archive or delete the OLD contaminated repo:

```bash
gh repo archive <old-owner>/<old-repo>
```

> A force-push to the existing repo (`git push --force --all origin`) also works, but GitHub may
> retain unreachable objects/cached views of the old commits. A new repo avoids that entirely.

## 6. Confirm

- [ ] Keys rotated in Azure; new values only in user-secrets / ignored files.
- [ ] Every check in step 4 prints nothing.
- [ ] The new repo's history (browse an old commit) shows no secrets/PII.
- [ ] The old contaminated repo is archived or deleted.
- [ ] The CI gitleaks job passes.

## Run it yourself

I (the assistant) do **not** force-push or run history rewrites on your remotes. The publish step
is yours:

```powershell
# Rotate keys in Azure first, then:
.\scripts\scrub-history.ps1 -NewRepo <owner>/<new-repo>   # scrub + verify + push to new repo
```

Or, to scrub and inspect first without pushing:

```powershell
.\scripts\scrub-history.ps1    # clones, scrubs, verifies; prints manual publish steps
```
