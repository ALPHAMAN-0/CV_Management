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

## P2 — Attribute library

### Decisions

**Domain entity: typed slots, not JSONB** (`src/CvPlatform.Domain/AttributeDefinition.cs`, `AttributeOption.cs`, `UserAttributeUsage.cs`)
- Why: each value type has its own slot (`StringValue`, `NumberValue`, `DateFrom`, `DateTo`, `BoolValue`, `OptionId`, `ImageUrl` + `ImagePublicId`). Queries stay SQL-translatable (LIKE on `StringValue`, `<` / `>` on `NumberValue` / `DateFrom` / `DateTo`, `OptionId IN (…)`), the index on `StringValue.SearchVector` (added in P6) is a real `tsvector`, and EF doesn't have to read a JSON document to render a row.
- Rejected: a single `Jsonb` column — no per-type validation, no SQL-side filtering without `jsonb_path_ops`, no FTS without re-parsing.

**`Type` is immutable once set** (`AttributeService.SaveAsync`)
- Why: changing a type would silently destroy data — the string value would disappear from the row but the option id or the number value would still be referenced by the form template.
- Rejected: a server-side "convert the value" tool — it would have to invent data (e.g. *Date → String* loses nothing; *String → Number* can fail). The error message tells the user to delete and recreate.

**Options are owned by the attribute** (`AttributeOption`, FK with cascade delete, owned collection)
- Why: a single `SaveChanges` commits or rolls back the attribute and all its options together, so partial state never reaches the database. Options can't exist without their attribute.
- Rejected: options as a global list (would have allowed re-use but made the "every option is a child of *this* dropdown" rule unenforceable).

**Versioned save + `SaveResult<T>`** (`Domain/SaveResult.cs`, `AttributeService.SaveAsync`)
- Why: matches the contract CLAUDE.md §6 lays out for every aggregate. Expected failures are results, not exceptions: `Saved(newVersion, T) | Conflict(serverState) | Invalid(errors) | Forbidden | NotFound`. The 3-way merge and the conflict dialog consume `Conflict` directly.
- The token (`AttributeDefinition.Version`, `int`) is the EF concurrency token. `SaveAsync` sets `Property(a => a.Version).OriginalValue = expectedVersion`, then `attribute.Version = expectedVersion + 1`. The `WHERE` clause of the UPDATE carries the version the editor loaded; a save committed between our read and this write throws `DbUpdateConcurrencyException` and is reported as `Conflict`. The version is bumped even when only options changed, because EF checks the token only on rows it updates.

**3-way merge on `Conflict`** (`AttributeConflictResolution.cs`)
- Why: an editor that has been idle may type into a field whose value someone else just changed. The component holds a base snapshot, the editor's dirty set, and the server state. A per-field rule: untouched locally → silently refreshed; changed on both sides → dialog: *Keep mine* (re-submit on the new version) / *Use theirs* (re-render with server state).
- Rejected: a "force overwrite" button (loses the other editor's work without asking) and a "your changes are lost" toast (the editor has no way to recover them).

**System attributes are seeded at startup, not via `HasData`** (`Data/Seed/SystemAttributes.cs`)
- Why: the four system attributes (`FirstName`, `LastName`, `Location`, `Photo`) are *library data* that an admin may rename or re-categorise. With `HasData` the migration would own those values; any later seed tweak would generate an `UpdateData` that overwrites the admin's edits. Rows are matched by `SystemKey` (an enum the seed and the migrations both reference) because ids differ per database and names are editable. The seeder only inserts missing keys — it never touches existing rows, so it is safe on every startup.
- Rejected: `HasData` + a one-time migration (couples data to migrations, fights admin edits).

**`Type` and `Category` stored as `string` (the enum member name)** (`AttributeDefinitionConfiguration`)
- Why: renaming a category or type in code is then a code change, not a destructive migration. Postgres enum types would couple the schema to a snapshot of the C# enum.
- Rejected: a Postgres `CREATE TYPE` (every enum rename is a `ALTER TYPE … RENAME VALUE`, and Postgres cannot drop a value in use).

**`AttributeLookup` is open to every signed-in user; `AttributeService` is gated to Recruiter/Admin** (`Features/Attributes/AttributeLookup.cs`, `AttributeService.cs`)
- Why: reading the attribute library is needed by every page that lets the user pick one (profile, position template, rule builder). Writing is an admin task. The split keeps the gate where the action is, and pages don't have to reinvent the authorization check.
- The `IAuthorizationService` call is in the service, not the page, per CLAUDE.md §3 ("Hiding UI is cosmetic").

**Prefix search is index-backed** (`AttributeDefinitionConfiguration`, `text_pattern_ops`)
- Why: the lookup is a short list — the user narrows it by typing, never by paging. A `LIKE 'TERM%'` becomes a `text_pattern_ops` range scan (`~>=~` / `~<~`), not a `Seq Scan`. `EXPLAIN ANALYZE` on a 4-row table already takes the index (`Buffers: shared hit=2` for the scan itself, `Execution Time: 0.159 ms`); it would scale identically to 4 000 rows.
- Rejected: `ILIKE '%term%'` (cannot use the index; would scan the whole table on every keystroke) and a `tsvector` (overkill for a category-and-prefix filter on a small table).

**"Recently used" is a per-user table, not a heap in memory** (`UserAttributeUsage`)
- Why: the list follows the user across circuits, devices and reloads; a heap would only live as long as the Blazor Server circuit. One composite-PK row per `(user, attribute)`, with a `(UserId, LastUsedAt DESC)` index for the read. The `MarkUsedAsync` race — two tabs that pick the same attribute at the same instant — is handled by `ExecuteUpdate` first (the common case), and an `INSERT` fallback that swallows `23505 unique_violation` (the loser of the race, which the test exercises by holding the winner's transaction open in a second DbContext).

**Two tables for the picker (library + recently-used) instead of one row with a `LastUsedAt` on `AttributeDefinition`**
- Why: the library is shared by every user; "recently used" is per user. Mixing them would either denormalise the library (a `LastUsedAt` per user, written on every pick) or leak the user's history into the shared read path.

### Likely questions
- *Why the typed slots instead of one `text` column?* — every filter and every FTS query is index-backed, the index plan is trivial to reason about, and the editor picks the right control per type without inspecting the value. A JSONB blob would re-introduce validation in code for every consumer.
- *Why no `HasData`?* — admin edits to system attributes (rename, re-categorise) would otherwise be silently undone by a future migration that re-seeds them.
- *Why a `Conflict` result, not an exception?* — exceptions are for unexpected failures. A stale save is *expected* on a multi-user edit screen; turning it into a result lets the dialog consume it directly.
- *Why do option changes also bump the version?* — EF checks the concurrency token only on the rows it actually UPDATE-s. If the option table didn't bump `AttributeDefinition.Version`, an option-only edit could overwrite another editor's `Name` change.
- *Why a separate table for "recently used"?* — it follows the user across circuits and devices; per-user data on a shared table is always a denormalisation smell.
- *Why swallow `23505`?* — the test `Two_simultaneous_picks_of_the_same_attribute_leave_one_row` demonstrates the race: two picks of the same attribute, the loser's INSERT must not propagate. Treating the duplicate-key as "the other pick already wrote this" matches the user's mental model.

### Change recipes
- *Add an attribute type*: add a value to `AttributeType`, an entry in `AttributeTypeCatalog` (slot, operators, completeness), a `*ValueEditor.razor` and `*Display.razor` under `Shared/`. The slots are typed at the database column, so adding a new type may also need a new column + a migration.
- *Add a rule operator*: add it to the per-type list in `AttributeTypeCatalog`, then the `PositionAccess` SQL builder — no UI switch needed, the rule builder reads the list.
- *Add a category*: add a value to `AttributeCategory`; the grid's category filter and the picker's category filter both pick it up automatically.
- *Change the autocomplete cap*: `AttributeLookup.MaxResults` (list page size) and `MaxRecent` (recently-used cap).
- *Tighten a duplicate-name rule*: edit `AttributeDefinition.NormalizeName`; the unique index is `IX_AttributeDefinitions_NormalizedName` (`text_pattern_ops`).
- *Convert the conflict dialog to a different UX*: every consumer goes through `AttributeConflictResolution.Resolve(base, mine, theirs)`; the dialog is the only `MudDialog` instance.

