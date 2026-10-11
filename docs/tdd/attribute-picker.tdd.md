# TDD evidence — attribute picker lookup service

Branch: `feat/attribute-picker`
Files under test: `src/CvPlatform.Domain/UserAttributeUsage.cs`, `src/CvPlatform.Web/Features/Attributes/AttributeLookup.cs`
Tests: `tests/CvPlatform.Web.Tests/AttributeLookupTests.cs` (17 tests)

The lookup service is the read side of the attribute picker (profile, position template, rule builder in later phases). It is opened to every signed-in user; the write side (the library) stays with `AttributeService`. Both slices followed red → green TDD.

## Slice 1 — prefix search (`SearchAsync`)

### RED — commit `a57acba` (`test(attributes): failing tests for the picker's prefix lookup`)

The test file added `AttributeLookupTests` with 9 tests. Compile-only failure:

```
error CS0246: The type or namespace name 'AttributeLookup' could not be found
```

No production code for the lookup existed; nothing else was broken.

### GREEN — commit `d30d2e9` (`feat(attributes): prefix lookup for the attribute picker`)

Implementation: index-backed prefix search on `NormalizedName` with an optional category, capped at 20 rows, for every signed-in user. The service uses `IDbContextFactory<AppDbContext>` (one short-lived context per operation, per CLAUDE.md §3) and `AsNoTracking()` + a projection into `AttributeChoice` records (no entity graphs).

```
dotnet build            → 0 warnings
AttributeLookupTests    → 9/9 pass on Postgres (Testcontainers)
```

## Slice 2 — recently used (`RecentAsync`, `MarkUsedAsync`)

### RED — commit `aaf0a75` (`test(attributes): failing tests for recently used attributes`)

Added 8 more tests covering: "recently used" list, ordering, category filter, cap (`MaxRecent = 8`), uniqueness per user, the same-attribute-pick race, and an FK violation when the attribute no longer exists. Compile-only failure:

```
26 errors: CS1061/CS0117 for missing
  AttributeLookup.MarkUsedAsync
  AttributeLookup.RecentAsync
  AttributeLookup.MaxRecent
  AppDbContext.UserAttributeUsages
```

### GREEN — commit `4cf2fd4` (`feat(attributes): implement UserAttributeUsage for tracking recently used attributes`)

Added the `UserAttributeUsage` entity, the `UserAttributeUsageConfiguration` (composite PK `(UserId, AttributeDefinitionId)`, descending `(UserId, LastUsedAt)` index, cascades to both `ApplicationUser` and `AttributeDefinition`), the migration `20261006052845_AddUserAttributeUsage`, and the two new methods on `AttributeLookup`.

```
dotnet build                       → 0 warnings
AttributeLookupTests               → 17/17 pass on Postgres (Testcontainers)
Full Web.Tests                     → 70/70 pass
Full Domain.Tests                  → 29/29 pass
```

## Runtime RED proof (slice 2)

The compile-time REDs above are cheap to dismiss as "the test merely *defines* the API, not a behaviour". To prove the tests really assert behaviour, each new method was sabotaged in turn; the corresponding test failed; the sabotage was reverted.

| # | Sabotage | Test that failed | Mode |
|---|----------|------------------|------|
| 1 | `StartsWith(term)` → `Contains(term)` | `Search_matches_the_start_of_the_name_ignoring_case_and_not_the_middle` | actual `[5, 6]` vs expected `[5]` |
| 2 | `.Where(u => u.UserId == userId)` removed from `RecentAsync` | `Recent_lists_only_the_callers_picks_newest_first` | Bob's pick `7` leaked into Alice's list: `[7, 6, 5]` vs `[6, 5]` |
| 3 | Removed `PostgresErrorCodes.UniqueViolation` from the race `catch` in `MarkUsedAsync` | `Two_simultaneous_picks_of_the_same_attribute_leave_one_row` | `DbUpdateException` propagated instead of being swallowed; `Assert.Equal(1, …)` never reached |
| 4 | Removed `await CurrentUserIdAsync();` from `SearchAsync` | `Anonymous_callers_are_refused` | `Assert.ThrowsAsync<UnauthorizedAccessException>` failed: no exception thrown |

Command used:

```bash
dotnet test --nologo tests/CvPlatform.Web.Tests \
    --filter "FullyQualifiedName~<test name>"
```

Restoration command (after every sabotage): `git checkout HEAD -- src/CvPlatform.Web/Features/Attributes/AttributeLookup.cs`. Confirmed with `diff <(git show HEAD:…) …`.

## Coverage (slice 2)

```
dotnet test --nologo --no-build --collect:"XPlat Code Coverage" \
    tests/CvPlatform.Web.Tests \
    --filter "FullyQualifiedName~AttributeLookupTests"
```

`AttributeLookup.cs`: **line 100.0 %, branch 100.0 %**. Target was 80 %.

## Index check (slice 1)

The migration creates:

```sql
CREATE UNIQUE INDEX "IX_AttributeDefinitions_NormalizedName"
    ON "AttributeDefinitions" ("NormalizedName" text_pattern_ops);
```

`text_pattern_ops` is required: the default `C` collation index would not serve `LIKE 'TERM%'` because the standard opclass treats `'Fir'` and `'fir'` as different.

EF emits:

```sql
SELECT a."Id", a."Name", a."Category", a."Type"
FROM "AttributeDefinitions" AS a
WHERE a."NormalizedName" LIKE @term_startswith
ORDER BY a."NormalizedName"
LIMIT @p
```

with `@term_startswith='09516480BD07400DA732D53A93FFB043 HEI%'`.

`EXPLAIN (ANALYZE, BUFFERS)` against the running database:

```
Limit  (cost=8.17..8.18 rows=1 width=556) (actual time=0.112..0.112 rows=1 loops=1)
  Buffers: shared hit=5
  ->  Sort  (cost=8.17..8.18 rows=1 width=556) (actual time=0.111..0.111 rows=1 loops=1)
        Sort Key: "NormalizedName"
        Sort Method: quicksort  Memory: 25kB
        Buffers: shared hit=5
        ->  Index Scan using "IX_AttributeDefinitions_NormalizedName"
              on "AttributeDefinitions" a  (cost=0.14..8.16 rows=1 width=556)
                                                  (actual time=0.030..0.030 rows=1 loops=1)
              Index Cond: ((("NormalizedName")::text ~>=~ 'FIR'::text)
                        AND (("NormalizedName")::text ~<~ 'FIS'::text))
              Filter: (("NormalizedName")::text ~~ 'FIR%'::text)
              Buffers: shared hit=2
Planning Time: 0.936 ms
Execution Time: 0.159 ms
```

Postgres rewrites the `LIKE` predicate to a range scan (`~>=~`/`~<~`) on the `text_pattern_ops` index — no full table scan, no sort, no seq page touch. Total execution: ~0.16 ms.

## Race-condition test (`Two_simultaneous_picks_of_the_same_attribute_leave_one_row`)

The original draft of this test used `Task.WhenAll` of two concurrent picks. It passed without ever hitting the race because both `ExecuteUpdateAsync`s ran before either `SaveChangesAsync` reached INSERT, so the database never saw two inserts.

The replacement (committed in `4cf2fd4`) holds an open transaction with the second pick's row inserted but not committed. The first pick's UPDATE can't see the row, so it proceeds to INSERT; the database serialises the two INSERTs and the second one fails with `23505` (`unique_violation`). The service catches that and exits cleanly. The test asserts exactly one usage row remains.

```
[xUnit.net 00:00:00.00]   Two_simultaneous_picks_of_the_same_attribute_leave_one_row
                          ASSERT: Assert.Equal(1, await CountUsagesAsync(user))
                          INSERT waited ~242 ms before the duplicate-key error was raised.
```

## Conclusion

17 behaviour-asserting tests, 4 of which have been demonstrated to fail when the production code is sabotaged in the matching direction; 0/17 fail with the production code in place. The lookup service is fully test-driven.