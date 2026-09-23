# CLAUDE.md — CV Management Platform (.NET)

Source of truth for requirements (verbatim assignment): @docs/REQUIREMENTS.md
This file defines HOW we build it. Defense notes you maintain: `docs/DEFENSE.md`.

## 0. How we work
- Graded capstone with a **live defense**: the author must explain every line and modify code on the spot. Optimize for least custom code, explicit over clever, small reviewable diffs. No speculative abstractions.
- Work strictly phase by phase (§13). Per phase: re-read the relevant requirements → plan (files, entities, packages, risks, requirement checklist) → **wait for approval** → implement in small commits → `dotnet build` (zero warnings) + `dotnet test` → run the app → update `docs/DEFENSE.md` → stop.
- `main` is always deployable.
- New dependency ⇒ state purpose, license, maintenance status. Use the latest stable versions compatible with .NET 10 and **verify component APIs against the installed version** (MudBlazor changes APIs between majors). Never guess signatures.
- Spec ambiguity ⇒ follow §12; if it isn't covered there, ask. Never silently reinterpret the spec.
- If a rule in this file looks wrong or outdated, say so before acting instead of quietly deviating.

## 1. Stack (fixed)
- .NET 10 LTS, C# latest, nullable enabled, warnings as errors.
- Blazor Web App: global **InteractiveServer**, **prerender disabled** (set in `App.razor` via `new InteractiveServerRenderMode(prerender: false)`; the template flag alone does not do it). Only the sign-in pages (`Login`, `ExternalLogin`) stay static SSR — they write cookies; the rest of the template's account pages were pruned in P1.
- ASP.NET Core Identity (cookies) + Google + GitHub external login. Core scope = external login only.
- EF Core 10 + Npgsql → PostgreSQL (Docker locally, managed Postgres in prod).
- MudBlazor for all UI: data grid, dialogs, autocomplete, chips, theming/dark mode.
- Markdown: an EasyMDE-based Blazor editor wrapper (e.g. PSC.Blazor.Components.MarkdownEditor); rendering via MudBlazor.Markdown, or Markdig (`DisableHtml()`) + HtmlSanitizer.
- Images: Cloudinary Upload Widget with **signed, direct browser → Cloudinary upload** (drag-and-drop built in). The server only signs upload parameters and stores `secure_url` + `public_id`. Image bytes never touch our server or DB.
- Tests: xUnit + Testcontainers.PostgreSql; bUnit only if a component genuinely needs it.
- Ops: Dockerfile, docker-compose (local Postgres), GitHub Actions (build, test, deploy), health checks.
- Later phases only: Bogus (demo seed, P7); QuestPDF + QRCoder, ClosedXML/CsvHelper, an SMTP provider (P8).
- Banned: MediatR, AutoMapper, generic repository/UoW wrappers over EF, lazy-loading proxies, EF InMemory provider, Newtonsoft.Json, raw SQL (only exception: parameterized, with a comment justifying it).

## 2. Solution layout
```text
Directory.Build.props          # Nullable, LangVersion, TreatWarningsAsErrors, analyzers
Directory.Packages.props       # central package versions
src/CvPlatform.Domain/         # entities, enums, AttributeTypeCatalog, pure logic (CV assembly,
                               # project ranking, completeness). No package references.
src/CvPlatform.Web/
  Data/                        # AppDbContext, IEntityTypeConfiguration<T>, Migrations/, Seed/
  Features/<Feature>/          # vertical slice: pages, components, <Feature>Service, DTO records
                               # Attributes, Profiles, Positions, Cvs, Discussions, Search, Home, Admin, Account
  Infrastructure/              # Cloudinary signing, IAppEvents bus, auth revalidation, culture/theme endpoints
  Components/                  # template root: App, Routes, Layout/ (layouts, app bar, nav), Account/ (sign-in pages)
  Shared/                      # per-type value editors/displays and other shared components (no .cs in a `Shared` namespace: CA1716)
  Resources/                   # SharedResource.<lang>.resx only; the English text is the key, so English needs no file
tests/CvPlatform.Domain.Tests/ # pure unit tests
tests/CvPlatform.Web.Tests/    # integration tests against real Postgres (Testcontainers)
docs/REQUIREMENTS.md  docs/DEFENSE.md
```
- Services depend on `IDbContextFactory<AppDbContext>` directly: EF Core already is the repository and unit of work.
- Entities are configured with Fluent API only; no EF attributes in Domain.

## 3. Blazor Server + EF Core rules (non-negotiable)
1. Components never inject `AppDbContext`. Services create one short-lived context per operation: `await using var db = await factory.CreateDbContextAsync(ct);`. A scoped context would live as long as the circuit → concurrent-use exceptions and stale tracking. (`AddDbContextFactory` also registers the scoped context Identity needs.)
2. Reads: `AsNoTracking()` + `Select` projection into DTO records. Never load entity graphs for lists.
3. **No queries in loops.** Batch with `Contains` (SQL `IN`), projections, `Include` + `AsSplitQuery`. Log SQL in Development and check it.
4. Every list is paged, sorted and filtered server-side (MudDataGrid `ServerData`). No unbounded queries.
5. Authorization is enforced inside services (resource-based). Hiding UI is cosmetic.
6. Off-thread UI updates go through `InvokeAsync(StateHasChanged)`. Every timer and subscription is released in `Dispose`/`DisposeAsync`.
7. `HttpContext` is unavailable in interactive components: read theme/culture cookies in `App.razor` during the initial request and pass them down.

## 4. Domain model
- `ApplicationUser : IdentityUser` — `PreferredCulture`, `PreferredTheme`, `CreatedAt`. Blocking = Identity lockout (`LockoutEnd = DateTimeOffset.MaxValue`).
- `Profile` (1:1 with user, PK = `UserId`) — `Version`, `UpdatedAt`; children `ProfileValues`, `Projects`. Created on first login with a row for every system attribute.
- `AttributeDefinition` — `Name`, `NormalizedName` (unique; prefix lookup), `Description`, `Category` (enum), `Type` (enum), `IsSystem`, `SystemKey?` (FirstName, LastName, Location, Photo), `Version`, `Options` (Dropdown only). Reserve an owned jsonb `Constraints` for the optional tuning phase.
- `AttributeOption` — `AttributeDefinitionId`, `Label`, `SortOrder`.
- `ProfileValue` — PK (`ProfileId`, `AttributeDefinitionId`). Typed slots: `StringValue` (String, Text/markdown), `NumberValue` (`numeric(18,4)`, never double), `DateFrom` (Date; Period start), `DateTo` (Period end; null = ongoing), `BoolValue` (null = unanswered), `OptionId`, `ImageUrl` + `ImagePublicId`.
  - Blank input is normalized to null on write, so *filled ⇔ the type's slot is non-null*. Persist `IsFilled` as a stored generated column.
  - `SearchVector`: generated tsvector over `StringValue`.
  - Row exists ⇔ the attribute is in the profile.
- `Project` — `ProfileId`, `Name`, `StartDate`, `EndDate?`, `Description` (markdown), tags (M:N), `SearchVector`.
- `Tag` — `Name`, `NormalizedName` (unique). Join tables `ProjectTag`, `PositionTag`.
- `Position` — `Title`, `ShortDescription`, `Company?`, `Level?` (enum), `AccessMode` (Public | Restricted), `MaxProjects` (0 = no projects section), `Version`, `CreatedAt`, `UpdatedAt`, `SearchVector`. Children: `PositionAttribute` (`AttributeDefinitionId`, `SortOrder`), `AccessRule` (`AttributeDefinitionId`, `Operator`, operand slots mirroring the value slots, `OptionIds int[]`), `PositionTag`.
- `Cv` — `ProfileId`, `PositionId` (unique pair), `Status` (Draft | Published), `CreatedAt`, `UpdatedAt`, `PublishedAt?`. **Stores no content.**
- `CvLike` — PK (`CvId`, `UserId`), `CreatedAt`.
- `DiscussionPost` — `Id` (bigint identity = ordering key), `PositionId`, `AuthorId?` (SET NULL on user delete), `Body` (markdown), `CreatedAt`.
- `UserAttributeUsage` — PK (`UserId`, `AttributeDefinitionId`), `LastUsedAt` → "recently used".
- Data Protection keys table (containers are ephemeral; lost keys log everyone out).

Per-type behaviour lives in one place: `AttributeTypeCatalog` in Domain (slot, allowed operators, completeness, validation) plus a Web-side map type → editor/display component, rendered via `DynamicComponent`. No `switch (type)` scattered across the UI. Adding a type = catalog entry + two components (+ a migration only if it needs a new slot).

Indexes beyond PKs/uniques: `Position(UpdatedAt DESC)`, `Cv(PositionId, Status)`, `Cv(CreatedAt)`, `DiscussionPost(PositionId, Id)`, `UserAttributeUsage(UserId, LastUsedAt DESC)`, GIN on every `SearchVector`. Prefix lookups on `NormalizedName` must be index-backed (`text_pattern_ops` or C collation).

## 5. Access rules (security boundary)
- One SQL-translatable specification: `PositionAccess.AccessibleTo(IQueryable<ProfileValue> candidateValues)` returns `Expression<Func<Position, bool>>` = `Public || AccessRules.All(rule satisfied by the candidate's value)`.
- Reused by: candidate position lists, the CV-creation guard, CV visibility (lists, CV page, search), home-page lists, tag-cloud targets. One query; no in-memory evaluation.
- Semantics: rules are AND-combined (keep the combinator isolated so `MatchMode.Any` is a small change); an absent or empty value fails every operator except `IsEmpty`; string comparisons are case-insensitive; Period `DateTo = null` means ongoing (+∞).
- Operators come from the catalog; the rule-builder UI renders the operator list and operand editor from it:

| Type | Operators |
|---|---|
| String | Equals, NotEquals, Contains, StartsWith, IsFilled, IsEmpty |
| Text | Contains, IsFilled, IsEmpty |
| Number | =, ≠, >, ≥, <, ≤, IsFilled, IsEmpty |
| Date | On, Before, After, IsFilled, IsEmpty |
| Period | Covers(date), StartsBefore, EndsAfter, IsFilled, IsEmpty |
| Boolean | IsTrue, IsFalse |
| Dropdown | Equals, NotEquals, AnyOf, IsFilled, IsEmpty |
| Image | IsFilled, IsEmpty |

- Integration test matrix: every type × operator × {match, mismatch, absent} on real Postgres.
- Position visibility: Anonymous → Public only (read-only). Candidate → accessible ones. Recruiter/Admin → all.

## 6. Optimistic concurrency + autosave
- Versioned aggregates: `Profile`, `Position`, `AttributeDefinition` — `int Version` as the EF concurrency token.
- Uniform save contract: `SaveAsync(id, expectedVersion, patchOrDto, ct)` → `SaveResult<T>` = `Saved(newVersion, T) | Conflict(serverState) | Invalid(errors) | Forbidden | NotFound`. Implementation: set the `Version` property's `OriginalValue` to `expectedVersion`, `Version++`, `SaveChanges`; `DbUpdateConcurrencyException` → `Conflict`.
- **Bump the root `Version` on every child change** (values, projects, template items, rules, options, tags). EF only checks the token on rows it actually updates.
- Autosave covers the profile page and the candidate's own-CV inline edits, through one write path (`ProfileService.ApplyPatchAsync`):
  - The component holds a base snapshot + a dirty set. Text inputs use `Immediate` + `DebounceInterval≈300ms`, so the dirty set is current while nothing hits the DB per keystroke.
  - `PeriodicTimer` every 7 s: if dirty, send one patch (dirty items only) → one transaction. Single-flight: saves never overlap. Also flush on navigation (`NavigationLock`) and, best-effort, in `DisposeAsync` (runs server-side, so it still fires after the tab closes, within circuit retention).
  - Status indicator: Saved hh:mm:ss / Unsaved / Saving… / Conflict / Error (retry with backoff).
  - Conflict: per-field 3-way merge (base/mine/theirs). Fields untouched locally refresh silently; fields changed on both sides → dialog: Keep mine (retry on the new version) / Use theirs.
- Positions and attributes use an explicit Save (shared, multi-editor resources; autosave there would manufacture conflicts) with the same conflict UX.

## 7. CV = projection, not a document
- A `Cv` row is only (candidate, position, status). Content is assembled on read from: the header (FirstName + LastName, always), the position's template attributes (sections grouped by Category, in template order; may include system attributes such as Photo or Location), and projects.
- Projects: candidate projects sharing ≥1 tag with the position, ranked by overlap count desc, then recency (ongoing first), take `MaxProjects`. Position without tags → most recent `MaxProjects`. `MaxProjects = 0` → no section.
- Build the CV view model with a fixed number of set-based queries (≤ 3). Assembly and ranking are pure Domain code with unit tests.
- Owner/Admin: every template attribute is editable in place with its type's editor; empty → red; edits go through profile autosave (adding the attribute to the profile if absent). Publish is enabled only when the name and all template attributes are filled, and is **re-checked server-side** inside the publish transaction. Unpublish → Draft.
- Recruiter: read-only render, empty → red, like toggle + count. Like = insert/delete on the composite PK; a duplicate-key race counts as success. Recruiters only ever see Published and visible CVs.
- Professional look: clear typographic hierarchy and a print stylesheet (the PDF phase reuses the same view model).

## 8. Search (PostgreSQL FTS)
- Stored generated `tsvector` + GIN on `Position` (Title, ShortDescription, Company), `ProfileValue` (StringValue), `Project` (Name, Description); tags matched by normalized name. Text-search config `simple` (multilingual user content; no stemming surprises).
- App-bar search on every page → `/search?q=` using `websearch_to_tsquery`.
- Results: Positions (access-filtered per §5) and CVs (Recruiter/Admin: Published + visible; Candidate: own). A CV matches only on content it renders: its template attributes' values, tag-matching projects, candidate name, position title. Each list is one paged, `ts_rank`-ordered query; CV rows show like counts.

## 9. Discussions (real-time)
- Append-only: ordering key = `DiscussionPost.Id`, never client time. No edits, no inserts between posts.
- In-process singleton pub/sub `IAppEvents` (one topic per position). Components subscribe on init and unsubscribe on dispose; on a signal they re-query `Id > lastSeenId` (gap-free after reconnects) and call `InvokeAsync(StateHasChanged)`.
- It sits behind an interface; the scale-out path (Postgres LISTEN/NOTIFY or Redis) is documented, not built.
- Author names link to the candidate page only for Recruiter/Admin viewers; deleted authors render as "Deleted user".
- Anonymous users may read discussions of public positions; posting requires sign-in and access to the position.

## 10. Auth, roles, admin
- Roles: Candidate (default on first login), Recruiter, Administrator — additive. Policies: `RecruiterOrAdmin`; resource-based `OwnerOrAdmin` handler (Admin passes every owner check, i.e. acts as the owner of every page).
- External sign-up creates the user straight from provider claims; `RequireConfirmedAccount` must not block external users. **Only provider-verified emails are trusted** (Google `email_verified`; GitHub `primary && verified` via `VerifiedEmailGitHubHandler`) because accounts are linked and admins bootstrapped by email. GitHub needs the `user:email` scope; the null-email path is an error redirect back to Login.
- Bootstrap: emails listed in `Admin:BootstrapEmails` receive Administrator **only when no admin exists** (a recovery path that doesn't fight self-demotion).
- User-management grid: view, block/unblock, delete, add/remove roles. An Admin may remove their own Admin role.
- Revocation takes effect immediately: update the security stamp; HTTP security-stamp validation interval ≈ 0; circuits use a revalidating auth-state provider with a short interval **plus push** — admin actions publish `UserInvalidated(userId)` on `IAppEvents` and affected circuits revalidate at once. A change to your own roles reloads through an endpoint that calls `RefreshSignInAsync`.
- Deleting a user cascades profile, CVs and likes; their discussion posts remain with `AuthorId = null`.
- Hosting: `UseForwardedHeaders` (X-Forwarded-Proto) or OAuth redirect URIs break behind the proxy; Data Protection keys persisted to the DB; WebSockets enabled on the host.

## 11. UI rules (graded — each violation costs 20%)
- Positions, CVs, attributes, users, projects: **tables only** — no tiles, cards or galleries, including on mobile (disable the grid's stacked/card breakpoint; horizontal scroll + hide secondary columns).
- **No buttons in table rows.** Checkbox selection + a toolbar above the grid (Edit/Duplicate enabled for exactly one selected row, Delete for ≥1). Row click opens details (a grid with no detail page yet, e.g. Users, selects on row click). Links inside cells are fine. Outside tables prefer toolbar + selection; hover- or long-press-revealed actions are acceptable; never static per-item buttons.
- Every page: app bar with logo, full-text search box, language switch, theme toggle, user menu; role-aware nav drawer; breadcrumbs on detail pages. (Exception: the static sign-in pages have the app bar but no drawer — nothing there can toggle it, and the logo links Home.)
- One shared "missing value" style (red), used everywhere.
- Attribute picker (shared by profile, template and rule builder): server-side prefix lookup, "Recently used", category filter.
- Tag input: MudAutocomplete (server prefix search, new values allowed) + chips; tags normalized.
- Tag cloud: a maintained renderer if one fits, else weighted chips; clicking a tag opens search by tag (CVs for Recruiter/Admin, positions for everyone else).
- i18n: every UI string via `IStringLocalizer<SharedResource>`; English + Bengali (`bn`). Switching culture = antiforgery POST endpoint that sets the culture cookie and saves `PreferredCulture`, then a full reload. User content is never translated.
- Theme: light/dark via `MudThemeProvider`; same mechanism as culture (POST endpoint → cookie, plus `PreferredTheme` for users → reload). Saved choices are restored into the cookies at sign-in.

## 12. Decisions on spec ambiguities (confirm with the mentor; update here if overruled)
1. Lost access → the CV is hidden from candidate lists, position CV lists and search for everyone except Admin (who sees a "hidden" marker). Nothing is deleted.
2. Anonymous users see Public positions only.
3. Boolean is tri-state: null = unanswered → red, and it blocks Publish.
4. A published CV is live: template changes after publishing show up as red gaps to recruiters; no auto-unpublish.
5. Attribute delete is forbidden if the attribute is a system attribute or referenced by any access rule (a library edit must never silently widen access); otherwise it cascades to template entries and profile values after a confirm dialog showing impact counts. `Type` is immutable once referenced.
6. The CV header always shows the name; everything else is template-driven (§7).
7. External providers: Google + GitHub.
8. Autosave scope: profile + own-CV inline edits; positions and attributes use explicit Save.
9. Second UI language: Bengali (`bn`). Swapping it is one resx file + the supported-cultures list.
10. Host: Render (Docker web service, WebSockets on, managed Postgres) via `render.yaml`; CI deploys through the `RENDER_DEPLOY_HOOK_URL` secret after tests pass.

## 13. Phases (each ends deployed, with DEFENSE.md updated)
- **P0 Walking skeleton** — `dotnet new blazor --interactivity Server --all-interactive --auth Individual`, restructured per §2. Deploy #1: bare "Hello, world" Home with no DB dependency. Deploy #2: Npgsql, Dockerfile, docker-compose, forwarded headers, Data Protection keys in DB, `/health` with a DB check, migrations applied at startup (single instance), CI build + test, deployed to Render.
- **P1 Identity & shell** — Google/GitHub, roles + bootstrap, profile auto-creation, prune unused Identity pages, app shell (search box stub, nav, theme, culture) with persistence, grid + toolbar pattern.
- **P2 Attribute library** — CRUD, options, categories, system-attribute seed, versioned save + conflict UX, attribute picker.
- **P3 Profile** — Me/Info/Projects, per-type editors/displays, Cloudinary upload, Markdown editor/renderer, tags, autosave + conflicts.
- **P4 Positions** — grid, create/duplicate/edit/delete, template ordering, tags, MaxProjects, Company/Level, rule builder, `PositionAccess` + its test matrix.
- **P5 CVs** — access-guarded creation, assembly, inline editing, publish/unpublish, visibility, the position's CV list, likes.
- **P6 Discussions, search, home** — real-time posts, FTS, latest / top-5 / tag cloud / stats (cached ~60 s), candidate page for recruiters.
- **P7 Admin & hardening** — user management, immediate revocation, responsive audit, full i18n pass, SQL/N+1 review, config-gated demo seed.
- **P8 Optional, only once all core works** — PDF + QR, email/password with confirmation, badges SVG, field constraints, CSV/Excel export.

## 14. Code style
- File-scoped namespaces, `sealed` by default, primary constructors for DI, `record` DTOs, `CancellationToken` on every async path.
- Expected failures are results (`SaveResult<T>`), not exceptions.
- Comments explain *why*, never *what*. No dead code, no TODO graveyards.
- Conventional commits, one concern per commit.

## 15. docs/DEFENSE.md (update every phase)
- Each decision: what, why, rejected alternatives, trade-off.
- Likely reviewer questions with short answers: the autosave + optimistic-locking flow; why `IDbContextFactory`; how access rules become SQL; typed-slot EAV vs JSONB; where N+1 is prevented; why image bytes never touch the server; Blazor Server's scaling limits and upgrade path.
- Change recipes for live drills: add an attribute type; add a rule operator; OR combinator; new grid column + sort; change the autosave interval; add a CV section; grant Recruiters a new permission.

## 16. Commands
- Local DB: `docker compose up -d db`
- Full stack in the production image: `docker compose --profile app up -d --build` → http://localhost:8080
- Run: `dotnet watch --project src/CvPlatform.Web`
- Build / test: `dotnet build` · `dotnet test` (Web tests need Docker running)
- Restore local tools once: `dotnet tool restore`
- Migration: `dotnet ef migrations add <Name> -p src/CvPlatform.Web -s src/CvPlatform.Web -o Data/Migrations`
