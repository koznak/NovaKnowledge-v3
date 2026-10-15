# Security Policy

## Reporting a Vulnerability

This is a portfolio / demonstration project, but security reports are still welcome.
If you find a vulnerability or an exposed credential, please **do not open a public issue**.

Instead, report it privately:

- Open a [GitHub security advisory](https://docs.github.com/en/code-security/security-advisories) for this repository, or
- Contact the maintainer directly through their GitHub profile.

Please include steps to reproduce and, if applicable, the affected file or endpoint.
You can expect an initial response within a few days.

## Scope

NovaKnowledge is a **single-tenant demonstration** of an enterprise RAG knowledge
copilot. It is **not hardened for production** and ships with **no authentication**
(see the "Scope & Limitations" section of the [README](../README.md)). Reports about
its deliberately out-of-scope areas (missing auth, unrestricted endpoints) are known
limitations rather than vulnerabilities.

## Secrets Handling

- Committed `appsettings.json` files contain **placeholders only** (empty keys,
  `YOUR-*-RESOURCE` endpoints).
- Real credentials must be supplied via `dotnet user-secrets` or an ignored local
  override (`appsettings.*.local.json`).
- CI runs [gitleaks](https://github.com/gitleaks/gitleaks) to block secret commits.
