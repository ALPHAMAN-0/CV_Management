# Unfinished work

Written 2026-10-06, when work on the attribute picker (the last open item of Phase 2) was paused.
Delete this file once everything below is done.

## Where things stand

| Branch | Head | State |
|---|---|---|
| `main` | `b55217b` | Green and deployable: build 0 warnings, 29 domain + 53 web tests pass. Nothing new merged. |
| `feat/attribute-picker` | `aaf0a75` + uncommitted changes | The picker's lookup service. Paused in the middle of slice 2 (see below). |
| `wip/audit-hardening` | `b03c305` | Parked, unplanned work. **The app does not start on this branch.** |

Nothing has been pushed. All three branches are local only.

## 1. `feat/attribute-picker` — paused mid-way

### Done and committed

- `a57acba` failing tests for the prefix search (RED: `AttributeLookup` did not exist).
- `d30d2e9` `AttributeLookup.SearchAsync`: prefix search on `NormalizedName`, optional category,
  at most 20 rows, any signed-in user (GREEN: 9/9 tests).
- `aaf0a75` failing tests for "Recently used" (RED: `MarkUsedAsync`, `RecentAsync`, `MaxRecent`
  and `UserAttributeUsages` did not exist).

### Done but NOT committed (sitting in the working tree)

- `src/CvPlatform.Domain/UserAttributeUsage.cs` — new entity.
- `src/CvPlatform.Web/Data/Configurations/UserAttributeUsageConfiguration.cs` — key, index, cascades.
- `src/CvPlatform.Web/Data/AppDbContext.cs` — the new `DbSet`.
- `src/CvPlatform.Web/Data/Migrations/20261006052845_AddUserAttributeUsage.cs` + `.Designer.cs`
  and the updated `AppDbContextModelSnapshot.cs` — generated with `dotnet ef`, not by hand.
- `src/CvPlatform.Web/Features/Attributes/AttributeLookup.cs` — `RecentAsync` and `MarkUsedAsync`.
- `tests/CvPlatform.Web.Tests/AttributeLookupTests.cs` — the race test was rewritten after the RED
  commit, because the first version (`Task.WhenAll` of two picks) passed without ever hitting the race.

Last check of this uncommitted state: `dotnet build` 0 warnings, and
`dotnet test --filter FullyQualifiedName~AttributeLookupTests` → 17 of 17 pass. In that run the
race test's INSERT waited 242 ms and then failed with a duplicate-key error (23505) that the service
swallowed, so the race branch really ran.

### Still to do on this branch

1. **Run the whole suite.** Only the 17 lookup tests were run after slice 2. The other 53 web tests
   and the 29 domain tests have not been rerun with the new migration in place.
2. **Commit slice 2** as the GREEN checkpoint (the files listed above).
3. **Prove the tests can fail.** Both RED steps were compile errors, so no test has yet been seen
   failing at run time. Planned check: break the code on purpose and confirm the matching test fails —
   `StartsWith` → `Contains`; drop the `UserId` filter in `RecentAsync`; remove `UniqueViolation`
   from the `catch` in `MarkUsedAsync`; skip the sign-in check. Then undo the changes.
4. **Coverage** for `AttributeLookup` (`dotnet test --collect:"XPlat Code Coverage"`); target 80%+.
5. **Index check.** Run one `EXPLAIN` on the prefix search to confirm it uses
   `IX_AttributeDefinitions_NormalizedName`, and record the result in `docs/DEFENSE.md`.
   The logged SQL is already as intended: `WHERE "NormalizedName" LIKE @term_startswith ... LIMIT 20`.
6. **`docs/DEFENSE.md`: the whole Phase 2 section is missing** — decisions, likely questions and
   change recipes for the attribute library, versioned save, conflict dialog, three-way merge,
   system-attribute seed and the lookup service.
7. **`CLAUDE.md` §13** — move "attribute picker" (the component) from P2 to P3, as agreed:
   P2 delivers the table and the lookup service, P3 starts with the component.
8. **TDD evidence report** — `docs/tdd/attribute-picker.tdd.md` (RED/GREEN commands and results).
9. **Merge into `main`** with `git merge --no-ff feat/attribute-picker`, so `main`'s own history
   never contains the two RED commits that do not build.
10. **Run the app and deploy.** Push, then Manual Deploy in Render (no deploy hook is set).

### Known weak spots to look at when resuming

- The race test waits a fixed 250 ms for the other call to reach its INSERT. If that were ever too
  short the test would still pass, only without proving the race. Step 3 above is what confirms it.
- `MarkUsedAsync` ignores a foreign-key error (attribute or user deleted meanwhile). Only the
  deleted-attribute case has a test.
- `AttributeLookup` is registered in `Program.cs` but nothing calls it yet. Its first caller is the
  picker component in P3.

## 2. Not started

- **`AttributePicker.razor`** (P3, first slice): a `MudAutocomplete` using `SearchFunc`, a
  "Recently used" header (`BeforeItemsTemplate`) and a category select. These members were confirmed
  to exist in MudBlazor 9.10.0. The component should call `MarkUsedAsync` when the user picks.
  Its new UI strings need Bengali entries in `SharedResource.bn.resx`.
- No browser check of the picker is possible until a page hosts it (the profile page in P3).

## 3. `wip/audit-hardening` — parked, decision still open

Holds unplanned work that was uncommitted on `main` on 2026-10-06: audit log, profile-view counting,
a one-shot bootstrap-admin table, security headers/CSP, page-size clamp, LIKE escaping.
It is outside the phase plan (CLAUDE.md §13) and the domain model (§4). Do not merge it as it is.

Problems found:

- **The app cannot start.** The migration `20261006000000_AddAuditAndProfileViews.cs` was written by
  hand: no `.Designer.cs`, snapshot not updated. `MigrateAsync` throws `PendingModelChangesWarning`
  and 52 of 61 web tests fail.
- **Changes the §10 bootstrap rule without saying so.** The grant becomes once per email, ever. A sole
  admin who removes their own role would leave the site with no admin and no way back.
- **Tests that prove nothing.** Most new domain tests only read back a property they just set.
  `ProfileViewTests` never calls `ProfileViewService.RecordAsync`. `AuditLogTests` passes the actor id
  as a second role, so it will fail once the app starts.
- **`LikePattern` is not needed.** `List_search_treats_wildcards_literally` already passes on `main`
  with plain `.Contains`.
- **Rule breaks.** `AuditLog.ListAsync` has no authorization check in the service (§3.5), returns
  entities instead of DTOs (§3.2), and its grid is not `ServerData` (§3.4). `ProfileViewService` has
  no caller.
- **CSP never checked in a browser.** `script-src 'self'` may block the inline `<ImportMap />` and
  will block the Cloudinary upload widget planned for P3.

Still to decide: drop the branch, or redo the useful parts test-first in the phase they belong to
(most of it is P7 hardening).

## 4. Small leftovers

- `.puku-cli/` is untracked and not in `.gitignore`.
- This file (`docs/UNFINISHED.md`) is itself untracked.

## How to resume

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
open -a Docker                      # the web tests need Docker
git switch feat/attribute-picker
dotnet build                        # expect 0 warnings
dotnet test                         # step 1 above: the full suite
```
