# Unfinished work

Updated 2026-10-11, after the verification loop closed every item in this file except the
`wip/audit-hardening` parking lot (a separate decision).

## Where things stand

| Branch | Head | State |
|---|---|---|
| `main` | `b55217b` | Green and deployable. Nothing new merged. |
| `feat/attribute-picker` | `85ed402` (pushed) | Slice 1 (prefix search) + slice 2 (recently used) shipped; all eight phase-2 items below closed. |
| `wip/audit-hardening` | `b03c305` | Parked, unplanned work. **The app does not start on this branch.** |

## 1. `feat/attribute-picker` — items closed in this verification run

| # | Item | Status | Evidence |
|---|------|--------|----------|
| 1 | Run the whole suite after the new migration | closed | `dotnet test` -> 29/29 Domain + 70/70 Web, all green |
| 2 | Commit slice 2 as the GREEN checkpoint | closed | `4cf2fd4 feat(attributes): implement UserAttributeUsage for tracking recently used attributes` |
| 3 | Prove the tests can fail (runtime RED evidence) | closed | 4 sabotages run on the running code, each confirmed the matching test failed, then reverted. Full table in `docs/tdd/attribute-picker.tdd.md` |
| 4 | Coverage for `AttributeLookup` >= 80% | closed | line 100.0 %, branch 100.0 % (target was 80 %) |
| 5 | `EXPLAIN` on the prefix search | closed | `Index Scan using IX_AttributeDefinitions_NormalizedName` (text_pattern_ops); 0.16 ms execution time. Full plan in `docs/tdd/attribute-picker.tdd.md` |
| 6 | `docs/DEFENSE.md` Phase 2 section | closed | "P2 - Attribute library" added with decisions, likely questions, change recipes |
| 7 | `CLAUDE.md` section 13 - picker component moves to P3 | closed | CLAUDE.md section 13 now says P2 ships the lookup service; the picker *component* is the first slice of P3 |
| 8 | TDD evidence report | closed | `docs/tdd/attribute-picker.tdd.md` |
| 9 | Merge into `main` (--no-ff) | pending | See "Next step" below |
| 10 | Deploy to Render | pending | See "Next step" below |

### Other items closed alongside

- `fix(verify): CRLF line endings and a flaky domain-dependency assertion` (commit `85ed402`) -
  `IdentityRevalidatingAuthenticationStateProvider.cs` was on CRLF in violation of `.editorconfig`;
  `DomainDependencyTests.Domain_references_only_the_base_class_library` was flaky because the test
  platform loads `netstandard` into the Domain assembly's reference closure when Web.Tests run in the
  same host. `netstandard*` is now filtered explicitly with a comment.

## 2. Not started (P3)

- **`AttributePicker.razor`** (P3, first slice): a `MudAutocomplete` using `SearchFunc`, a
  "Recently used" header (`BeforeItemsTemplate`) and a category select. These members were confirmed
  to exist in MudBlazor 9.10.0. The component should call `MarkUsedAsync` when the user picks.
  Its new UI strings need Bengali entries in `SharedResource.bn.resx`.
- No browser check of the picker is possible until a page hosts it (the profile page in P3).

## 3. `wip/audit-hardening` - parked, decision still open

Holds unplanned work that was uncommitted on `main` on 2026-10-06: audit log, profile-view counting,
a one-shot bootstrap-admin table, security headers/CSP, page-size clamp, LIKE escaping.
It is outside the phase plan (CLAUDE.md section 13) and the domain model (section 4). Do not merge it as is.

Problems found:

- **The app cannot start.** The migration `20261006000000_AddAuditAndProfileViews.cs` was written by
  hand: no `.Designer.cs`, snapshot not updated. `MigrateAsync` throws `PendingModelChangesWarning`
  and 52 of 61 web tests fail.
- **Changes the section 10 bootstrap rule without saying so.** The grant becomes once per email, ever. A sole
  admin who removes their own role would leave the site with no admin and no way back.
- **Tests that prove nothing.** Most new domain tests only read back a property they just set.
  `ProfileViewTests` never calls `ProfileViewService.RecordAsync`. `AuditLogTests` passes the actor id
  as a second role, so it will fail once the app starts.
- **`LikePattern` is not needed.** `List_search_treats_wildcards_literally` already passes on `main`
  with plain `.Contains`.
- **Rule breaks.** `AuditLog.ListAsync` has no authorization check in the service (section 3.5), returns
  entities instead of DTOs (section 3.2), and its grid is not `ServerData` (section 3.4). `ProfileViewService` has
  no caller.
- **CSP never checked in a browser.** `script-src 'self'` may block the inline `<ImportMap />` and
  will block the Cloudinary upload widget planned for P3.

Still to decide: drop the branch, or redo the useful parts test-first in the phase they belong to
(most of it is P7 hardening).

## 4. Small leftovers

- `.puku-cli/` is untracked and not in `.gitignore`. (Harness state, not project state.)
- This file (`docs/UNFINISHED.md`) is itself untracked.

## How to resume after merge

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
open -a Docker                      # the web tests need Docker
git switch main
git merge --no-ff feat/attribute-picker
git push origin main
# Then: Render -> Manual Deploy.
```
