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

## P1 — Identity & app shell

### Decisions

**Sign-in packages:** `Microsoft.AspNetCore.Authentication.Google` (Microsoft, MIT) and `AspNet.Security.OAuth.GitHub` (aspnet-contrib, Apache-2.0, maintained; GitHub has no Microsoft package). **MudBlazor 9.10** (MIT, a release every 2–4 weeks) for all UI.

**Providers register only when configured** (`Program.cs`): a missing `Authentication:Google` section simply skips `AddGoogle`. Local runs and CI tests need no secrets.
- Secrets: Render environment variables (`Authentication__Google__ClientId` …, `__` = `:`), `dotnet user-secrets` locally. Never in `appsettings.json` or git.

**Only provider-verified emails are trusted** (`Program.cs`, `Infrastructure/VerifiedEmailGitHubHandler.cs`)
- Why: accounts are created and *linked* by email, and the bootstrap admin is granted by email. An unverified address would let someone claim another person's account.
- Google: the email claim is mapped only when `email_verified` is `true`.
- GitHub: the stock handler takes the *primary* address from `/user/emails` without checking `verified`. A 15-line subclass overrides `GetEmailAsync` to require `primary && verified`, and the profile's public `email` mapping is removed so the check always runs.
- `RequireUniqueEmail = true` makes "one email = one account" hold in the database too.
- Rejected: dropping link-by-email (the same person would get two unrelated profiles).

**`ExternalLogin` is redirect-only** (`Components/Account/Pages/ExternalLogin.razor`)
- Known login → set up → sign in. New login → find the user by (verified) email or create one with `EmailConfirmed = true` → link → set up → sign in.
- The provider's error text (`?RemoteError=`) is logged, never shown: anyone can craft that URL, and the status cookie is `SameSite=Lax` so it survives the redirect back from Google/GitHub. Identity's own error texts are logged too; users see a localized message.
- No verified email → back to Login with an error. The template's "type an email" form is gone: that address would be unverified, and it was the only path that needed email confirmation. Every account therefore has a confirmed email and `RequireConfirmedAccount` never blocks an external user (CLAUDE.md §10).
- A blocked (locked-out) user can't get back in by linking a second provider.

**Roles and onboarding** (`Features/Account/AppRoles.cs`, `UserOnboarding.cs`)
- `AddRoles<IdentityRole>()` must come before `AddEntityFrameworkStores`: that call picks the role-aware stores only if a role type is set. The role tables already existed in the Initial migration, so no schema change.
- Roles are seeded at startup after migrations.
- `UserOnboarding.EnsureSetUpAsync` runs on **every** sign-in, **before** the cookie is issued, so the cookie already carries the roles. It is idempotent, so it also repaired accounts created before it existed:
  1. add Candidate if missing;
  2. add Administrator if the email is in `Admin:BootstrapEmails` **and no administrator exists** — a recovery path, not a permanent grant, so an admin who removes their own role isn't silently re-promoted;
  3. create the `Profile` row if missing.
- Rejected: seeding a fixed admin account (a password or email in code/config that grants power forever).

**Pruned Identity** — external sign-in only
- Deleted: register, password, email confirmation, 2FA, passkey and Manage pages, their endpoints, the no-op email sender, and `AddDefaultTokenProviders` (tokens serve only those flows; the security-stamp check doesn't need them).
- Kept `IdentitySchemaVersions.Version3` so the model (and next migration) doesn't drop the passkey table.

**Static vs interactive pages**
- Only `Login` and `ExternalLogin` stay static SSR (`[ExcludeFromInteractiveRouting]`): they write cookies, which needs the HTTP response. They use `AccountLayout`: app bar, no drawer (a drawer needs a circuit to toggle, and an anonymous visitor's only destination, Home, is the logo link).
- Everything else, including `AccessDenied` and `Lockout`, is interactive (`MainLayout`).
- `AppBar` is shared by both layouts, so every action in it is a plain link or form that works without a circuit: search is a GET form, language/theme are POST forms, sign-out is the Identity POST form. Only the signed-in `MudMenu` needs interactivity, and signed-in users never see the static pages (Login redirects them away).

**Language and theme** (`Infrastructure/Preferences.cs`)
- Both are cookies, because only the first HTTP request of a page load can read them (`App.razor`); the circuit can't see `HttpContext` (CLAUDE.md §3.7). `UseRequestLocalization` reads the culture cookie; `App.razor` reads the theme cookie and passes `DarkMode` through `Routes` as a cascading value.
- `App.razor` also renders a `MudThemeProvider` itself, so the very first response already has the theme's colours: no white flash in dark mode while the circuit connects, and static pages need no provider of their own.
- Switching = `POST /Preferences/Culture|Theme` with an antiforgery token (form binding makes minimal APIs require it) → set cookie → save `PreferredCulture`/`PreferredTheme` for signed-in users → redirect back (local URLs only) → the page reloads.
- The reload is required for culture: a circuit's culture is fixed when it starts. The theme reuses the same path: one mechanism, no JavaScript, works on static pages too. Cost: one reload on a rare action.
- At sign-in `Preferences.RestoreCookies` copies the saved choices into the cookies, so they follow the user to other browsers.
- Rejected: GET endpoints (a link on another site could change a signed-in user's saved settings); flipping the theme in place with JS cookie writes (a second code path for static pages).

**Strings** — every UI string is `L["English text"]` (`IStringLocalizer<SharedResource>` injected in the root `_Imports.razor`). Only `Resources/SharedResource.bn.resx` exists: a missing key renders the key itself, so English needs no file.
- MudBlazor's built-in texts (pager, column menu, checkbox labels) go through `Infrastructure/SharedMudLocalizer.cs`, a one-line `MudLocalizer` that looks MudBlazor's keys (`MudDataGridPager_RowsPerPage`, …) up in the same resx. MudBlazor keeps its own English; a missing Bengali key falls back to it.

**Grid + toolbar pattern** (`Features/Admin/Users.razor`, `UserAdminService.cs`)
- `MudDataGrid` with `ServerData`: paging, sorting and filtering run in the database. Each load is **two SQL statements** — a `COUNT` and one page query whose role flags are `EXISTS` subqueries (no N+1; checked in the SQL log).
- Only whitelisted columns sort (anything else → newest first), then `ThenBy(Id)` so rows with equal values (e.g. users backfilled with the same `CreatedAt`) page stably.
- A new search term goes back to page 1; otherwise the grid would ask for the old page of the new result set and could show "No users found" while matches exist.
- Checkbox selection, actions in a toolbar above the grid (never in rows), `Breakpoint.None` so phones get a scrollable table, not cards.
- Rows are a `record`, so re-fetched rows equal the selected ones and selection survives paging.
- The service checks the Administrator role itself (the page attribute is not the security boundary).
- No toolbar actions yet: block/roles/delete need P7's immediate-revocation rules. Row click selects until a user detail page exists.

**Routes** — a signed-in user without the page's role sees "Access denied" instead of being sent to sign in again (which would loop).

### Likely questions
- *Where do OAuth redirect URIs come from?* — `/signin-google` and `/signin-github` are the handlers' default `CallbackPath`. Scheme and host come from the request; behind Render's proxy `UseForwardedHeaders` makes the scheme `https` — **provided it runs before authentication** (see the incident below).
- *Why is linking by email safe?* — only emails the provider marks as verified are accepted (Google `email_verified`; GitHub `verified` via our handler), and emails are unique per user. An attacker would need control of the victim's mailbox.
- *How does a new user become Candidate before the first page loads?* — `EnsureSetUpAsync` runs before `SignInAsync`, and role claims are written into the cookie at sign-in.
- *Why do role changes need a new sign-in?* — role claims live in the cookie; they refresh at sign-in or when the security stamp is revalidated. Immediate revocation (P7) updates the stamp and pushes a revalidation.
- *Why POST for switching a theme?* — it also writes to the database for signed-in users; a state-changing GET is CSRF-able because the auth cookie is `SameSite=Lax`.
- *How is the grid kept from loading the whole table?* — `ServerData` hands `Page`/`PageSize`/sort to the service, which applies `Skip/Take` in SQL.

### Change recipes
- *Add a grid column + sort*: add the field to `UserRow` and to the `Select`; add a `PropertyColumn`; add a `(nameof(UserRow.X), …)` case to the sort switch.
- *Add a toolbar action*: a `MudButton` in `ToolBarContent` with `Disabled="@(selected is not { Count: > 0 })"`, calling a new service method that checks the role and takes the selected ids.
- *Add a language*: add the code to `Preferences.Cultures`, add `SharedResource.<code>.resx`; with more than two languages the switch becomes a `MudMenu` of forms.
- *Grant Recruiters a page*: `@attribute [Authorize(Roles = $"{AppRoles.Recruiter},{AppRoles.Administrator}")]` on the page, the same check in its service, and an `AuthorizeView Roles=…` around its nav link.
- *Add another provider (e.g. Microsoft)*: package in `Directory.Packages.props` + project, an `if (section.Exists())` block with a verified-email claim mapping, two env vars. The login page lists every registered scheme.

## Incident — OAuth callbacks saw `http://` behind the proxy (fixed)
- **Symptom (found in review, before anyone could sign in on Render):** the challenge sent `redirect_uri=https://…/signin-google`, but the callback's code exchange would send `http://…`, which the provider rejects.
- **Cause:** `Program.cs` never called `UseAuthentication`, so `WebApplication` inserted it automatically — *before* all of our middleware, including `UseForwardedHeaders`. OAuth callbacks are handled inside the authentication middleware, so they saw the proxy's plain-http request.
- **Fix:** explicit `app.UseAuthentication(); app.UseAuthorization();` after `UseHttpsRedirection`.
- **Guard:** `UserAdminTests.Anonymous_request_behind_a_tls_proxy_is_redirected_to_https_login` fails on the old pipeline (checked by reverting the fix).

## Review — Phase 1 (multi-agent, adversarially verified)
Four reviewers (correctness, security, project rules, i18n/UX) read the whole diff; a skeptic per dimension tried to refute each finding. 11 survived, all low/medium, all fixed before pushing: search not resetting the page; provider error text reflected on the sign-in page; MudBlazor texts and Identity errors not localized; three screen-reader labels; dark-mode flash before the circuit connects; the sign-in page's missing drawer (documented as an exception above). Six findings were refuted (e.g. "Candidate is re-added on every sign-in": nothing can remove it until P7).

## Incident — unverified GitHub email trusted (fixed)
- **Cause:** `AspNet.Security.OAuth.GitHub` picks the primary address without checking `verified`; with link-by-email that allowed account takeover and a race for the bootstrap admin role.
- **Fix:** `VerifiedEmailGitHubHandler` + Google `email_verified` mapping (see Decisions).
- **Lesson:** "the provider verified it" must be checked in code, not assumed.

## Incident — blank page in production (fixed)
- **Symptom:** the live site rendered a white page; `/health` was fine.
- **Cause:** with prerendering off, the server sends an empty shell and `blazor.web.js` draws everything. In .NET 10 that script ships in an implicit package (`Microsoft.AspNetCore.App.Internal.Assets`) that the SDK adds only when it sees Razor components. The Dockerfile restored from the `.csproj` files alone (a layer-caching trick), then published with `--no-restore`, so the package was never restored and the script returned 404.
- **Fix:** `dotnet publish` restores again after the full source is copied (the csproj-only restore still warms the cache).
- **Guard:** the CI `docker` job fails if the image lacks `wwwroot/_framework/blazor.web.js`.
- **Lesson:** a green `/health` doesn't prove the UI works; check the page in a browser after each deploy.
