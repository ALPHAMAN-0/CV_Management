# CV Management Platform

A Blazor Server (.NET 10) platform where candidates build a structured profile once and generate position-specific CVs from it; recruiters define positions (attribute templates + access rules), browse published CVs, like them, and discuss positions in real time.

**Stack:** .NET 10 · Blazor Web App (Interactive Server) · ASP.NET Core Identity (Google, GitHub) · EF Core 10 + PostgreSQL · MudBlazor · xUnit + Testcontainers · Docker · GitHub Actions · Render

## Run locally
```bash
docker compose up -d db                      # PostgreSQL 17
dotnet tool restore
dotnet watch --project src/CvPlatform.Web    # migrations apply on startup
```
Or the production image: `docker compose --profile app up -d --build` → http://localhost:8080 (`/health` for the DB check).

## Test
```bash
dotnet test    # integration tests start their own Postgres container (Docker required)
```

## Deploy (Render)
1. Render → **New → Blueprint** → select this repo (`render.yaml` creates the web service and database).
2. Copy the service's **Deploy Hook** URL into the GitHub secret `RENDER_DEPLOY_HOOK_URL`.
3. Every green build on `main` deploys.

## Docs
- [`CLAUDE.md`](CLAUDE.md) — architecture rules and phase plan
- [`docs/DEFENSE.md`](docs/DEFENSE.md) — decisions, trade-offs, reviewer Q&A
- [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md) — the assignment

## License
[MIT](LICENSE)
