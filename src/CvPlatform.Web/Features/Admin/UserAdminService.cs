using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace CvPlatform.Web.Features.Admin;

// A record so rows re-fetched on every page load still equal the selected ones.
public sealed record UserRow(
    string Id, string Email, DateTimeOffset CreatedAt, bool IsRecruiter, bool IsAdministrator, bool IsBlocked);

public sealed class UserAdminService(
    IDbContextFactory<AppDbContext> dbFactory,
    AuthenticationStateProvider authenticationState)
{
    // Paged, sorted and filtered in the database: one COUNT and one page query, whatever the page size.
    public async Task<GridData<UserRow>> ListAsync(string? search, GridState<UserRow> state, CancellationToken ct)
    {
        await EnsureAdministratorAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            users = users.Where(u => u.NormalizedEmail!.Contains(term));
        }

        var total = await users.CountAsync(ct);

        // Only whitelisted columns can be sorted; anything else falls back to newest first.
        var sort = state.SortDefinitions.FirstOrDefault();
        var ordered = (sort?.SortBy, sort?.Descending) switch
        {
            (nameof(UserRow.Email), true) => users.OrderByDescending(u => u.Email),
            (nameof(UserRow.Email), _) => users.OrderBy(u => u.Email),
            (nameof(UserRow.CreatedAt), false) => users.OrderBy(u => u.CreatedAt),
            _ => users.OrderByDescending(u => u.CreatedAt),
        };

        var now = DateTimeOffset.UtcNow;
        var items = await ordered
            .ThenBy(u => u.Id) // ties (e.g. users backfilled with the same CreatedAt) would make paging unstable
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            // Role flags are EXISTS subqueries inside the same SELECT, not a query per row.
            .Select(u => new UserRow(
                u.Id,
                u.Email!,
                u.CreatedAt,
                db.UserRoles.Any(ur => ur.UserId == u.Id
                    && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Recruiter)),
                db.UserRoles.Any(ur => ur.UserId == u.Id
                    && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Administrator)),
                u.LockoutEnd > now))
            .ToListAsync(ct);

        return new GridData<UserRow> { Items = items, TotalItems = total };
    }

    // Enforced here and not only on the page: hiding UI is cosmetic.
    private async Task EnsureAdministratorAsync()
    {
        var user = (await authenticationState.GetAuthenticationStateAsync()).User;
        if (!user.IsInRole(AppRoles.Administrator))
        {
            throw new UnauthorizedAccessException("Only administrators can list users.");
        }
    }
}
