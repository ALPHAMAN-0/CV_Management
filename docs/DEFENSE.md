# Defense notes

Per phase: each decision (what, why, rejected alternatives, trade-off), likely reviewer questions, and change recipes.

## P0 — Walking skeleton

### Decisions

**Template: `dotnet new blazor --interactivity Server --all-interactive --auth Individual`**
- Why: generates Identity (cookies, account pages, revalidating auth-state provider) that we'd otherwise hand-write.
- Rejected: an empty template + manual Identity wiring (more code to defend, same result).
- Trade-off: the template ships pages we don't need; P1 prunes them.

**Prerender off (`new InteractiveServerRenderMode(prerender: false)` in `App.razor`)**
- Why: with prerender on, every page renders twice (static pass, then interactive), running its queries twice and forcing state-persistence plumbing.
- Rejected: prerender + `PersistentComponentState` (extra code on every page).
- Trade-off: the first paint waits for the circuit; no SEO-visible HTML for interactive pages (not a requirement).
- Identity account pages still render as static SSR: they opt out via `AcceptsInteractiveRouting()`, because they must write cookies over HTTP.

**PostgreSQL via Npgsql, `AddDbContextFactory<AppDbContext>`**
- Why a factory: a Blazor Server DI scope lives as long as the circuit (the browser tab). A scoped `DbContext` would be shared across concurrent event handlers → "a second operation was started on this context" and stale tracked entities. One short-lived context per operation avoids both.
- `AddDbContextFactory` also registers a scoped `AppDbContext`, which Identity's stores use during HTTP requests (fine: there the scope is one request).

**Migrations applied at startup**
- Why: one deploy step; the app is single-instance.
- Rejected: a separate migration job / `efbundle` (needed only when several instances start at once and race).
- Trade-off: must revisit if we scale out.

**Data Protection keys stored in the database (`IDataProtectionKeyContext`)**
- Why: keys encrypt the auth and antiforgery cookies. Containers are ephemeral; file-system keys disappear on every redeploy and everyone is logged out.
- Rejected: a mounted volume (not available on the free host), Azure Key Vault/Blob (extra service).

**Forwarded headers (X-Forwarded-For/Proto), known proxies cleared**
- Why: the host terminates TLS at its proxy. Without X-Forwarded-Proto the app believes it runs on `http` and builds `http://` OAuth redirect URIs that Google/GitHub reject.
- Known proxies are cleared because the host's proxy IPs aren't published; the container is only reachable through that proxy.

**`/health` with a Postgres check (`AspNetCore.HealthChecks.NpgSql`, Apache-2.0, actively maintained)**
- Why: the host's health probe restarts the container if the DB link breaks; CI integration test asserts it.

**`postgres://` URI normalization (`Infrastructure/PostgresConnectionString`)**
- Why: Render (like Neon/Heroku) exposes the connection string as a URI; Npgsql only parses `key=value`. A 20-line converter beats asking operators to hand-assemble strings.

**Host: Render (Docker web service + managed Postgres, `render.yaml`)**
- Why: native Docker, WebSockets on by default (Blazor Server needs them), managed Postgres, free tier, infra-as-code blueprint.
- Rejected: Azure App Service (more setup, paid for always-on), Fly.io (needs a credit card, more ops).
- Trade-off: the free web service sleeps when idle (cold start ~1 min); free Postgres has a limited lifetime — upgrade the plan before the defense.
- Deploy flow: CI builds, tests (Testcontainers), builds the Docker image, then POSTs the Render deploy hook (`RENDER_DEPLOY_HOOK_URL` secret). `autoDeploy` is off so broken commits never deploy.
- Deploy #1 (no-DB Hello world) was folded into Deploy #2: the DB-backed skeleton was ready before a host account existed.

**Build hygiene**
- `Directory.Build.props`: net10.0, nullable, warnings as errors, `latest-recommended` analyzers, code style enforced in build.
- `Directory.Packages.props`: one place for every package version.
- `.editorconfig` disables CA1848/CA1873 (LoggerMessage source generators): ceremony that doesn't pay off at this log volume. Migrations are marked generated code.
- `global.json` pins the .NET 10 SDK band; `dotnet-tools.json` pins `dotnet-ef`.

**Tests**
- `Domain.Tests`: asserts the Domain assembly references nothing beyond the BCL (enforces §2).
- `Web.Tests`: `WebApplicationFactory<Program>` + a real Postgres container (Testcontainers) — startup migrations run, `/health` returns Healthy. No EF InMemory provider: it doesn't enforce constraints, transactions or SQL translation, so it would give false confidence.

### Likely questions
- *Why `IDbContextFactory`?* — see above: circuit-lifetime scope vs. one context per operation.
- *What happens if Data Protection keys are lost?* — every auth cookie becomes undecryptable → all users are signed out; antiforgery tokens in open forms fail.
- *Why no prerender?* — double execution and double queries; we don't need SEO.
- *Blazor Server scaling limits?* — each user holds a circuit (memory + a WebSocket) on one server. Scale-out needs sticky sessions (or Azure SignalR Service) and a shared event bus (Postgres LISTEN/NOTIFY or Redis) behind `IAppEvents`.

### Change recipes
- *Change the DB host*: set `ConnectionStrings__DefaultConnection` (URI or key=value).
- *Add a health check*: `builder.Services.AddHealthChecks().Add…()` in `Program.cs`.
- *Add a package*: add `<PackageVersion>` to `Directory.Packages.props`, then `<PackageReference Include="…" />` (no version) in the project.
