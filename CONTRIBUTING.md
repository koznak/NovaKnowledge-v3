# Contributing

Thanks for your interest in NovaKnowledge. This is primarily a portfolio/reference project, but
issues and pull requests are welcome.

## Getting set up

1. Install the .NET 10 SDK, Docker Desktop, and Python 3.12.
2. `docker compose up -d postgres redis azurite servicebus-sql servicebus-emulator`
3. Configure credentials with `dotnet user-secrets` — **never** commit real keys
   (see [SECURITY.md](SECURITY.md)). Committed `appsettings.json` files must contain placeholders.
4. Run `dotnet build NovaKnowledge.sln` and `dotnet test NovaKnowledge.sln`.

## Before you open a PR

- **Build and test**: `dotnet build NovaKnowledge.sln` and `dotnet test NovaKnowledge.sln` must pass.
- **Python changes**: run `pytest python/evaluation-service -q`.
- **Formatting**: `dotnet format NovaKnowledge.sln` (CI verifies with `--verify-no-changes`).
- **No secrets**: CI runs gitleaks; do not bypass it.
- Keep commits focused and use conventional prefixes (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).

## Scope

The repository intentionally ships as a **single-tenant demo without authentication**. Changes that
add auth should be discussed in an issue first so they stay coherent with the documented scope.

## Reporting issues

Use the issue templates under `.github/ISSUE_TEMPLATE/`. For security reports, follow
[SECURITY.md](SECURITY.md) instead of opening a public issue.
