using System.Security.Claims;
using CvPlatform.Domain;
using CvPlatform.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CvPlatform.Web.Features.Attributes;

// The read side of the attribute picker (profile, position template, rule builder). Open to every
// signed-in user, candidates included; changing the library stays with AttributeService.
public sealed class AttributeLookup(
    IDbContextFactory<AppDbContext> dbFactory,
    AuthenticationStateProvider authenticationState)
{
    // A picker shows a short list: the user narrows it by typing, never by paging.
    public const int MaxResults = 20;

    // Few enough to take in at a glance; older picks are still found by typing.
    public const int MaxRecent = 8;

    public async Task<IReadOnlyList<AttributeChoice>> SearchAsync(
        string? prefix, AttributeCategory? category, CancellationToken ct)
    {
        // Every signed-in user gets the same answer; the id only proves there is one.
        await CurrentUserIdAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attributes = db.AttributeDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            // StartsWith becomes LIKE 'TERM%', which the text_pattern_ops index on NormalizedName
            // serves. EF escapes % and _ in the term, so they match only themselves.
            var term = AttributeDefinition.NormalizeName(prefix);
            attributes = attributes.Where(a => a.NormalizedName.StartsWith(term));
        }
        if (category is not null)
        {
            attributes = attributes.Where(a => a.Category == category);
        }

        return await attributes
            .OrderBy(a => a.NormalizedName)
            .Take(MaxResults)
            .Select(a => new AttributeChoice(a.Id, a.Name, a.Category, a.Type))
            .ToListAsync(ct);
    }

    // The caller's own picks, newest first. One query: the usage rows joined to their attributes.
    public async Task<IReadOnlyList<AttributeChoice>> RecentAsync(AttributeCategory? category, CancellationToken ct)
    {
        var userId = await CurrentUserIdAsync();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attributes = db.AttributeDefinitions.AsNoTracking();
        if (category is not null)
        {
            attributes = attributes.Where(a => a.Category == category);
        }

        return await db.UserAttributeUsages
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Join(attributes, u => u.AttributeDefinitionId, a => a.Id, (u, a) => new { u.LastUsedAt, Attribute = a })
            .OrderByDescending(x => x.LastUsedAt)
            .Take(MaxRecent)
            .Select(x => new AttributeChoice(x.Attribute.Id, x.Attribute.Name, x.Attribute.Category, x.Attribute.Type))
            .ToListAsync(ct);
    }

    // Remembers that the caller picked this attribute. The update comes first because most picks
    // repeat an earlier one; only a first pick inserts.
    public async Task MarkUsedAsync(int attributeId, CancellationToken ct)
    {
        var userId = await CurrentUserIdAsync();
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.UserAttributeUsages
            .Where(u => u.UserId == userId && u.AttributeDefinitionId == attributeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastUsedAt, now), ct);
        if (updated > 0)
        {
            return;
        }

        db.UserAttributeUsages.Add(
            new UserAttributeUsage { UserId = userId, AttributeDefinitionId = attributeId, LastUsedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation
        })
        {
            // Unique: the same pick, made at the same moment, inserted the row first with the same time.
            // Foreign key: the attribute or the user was deleted meanwhile, so there is nothing to remember.
        }
    }

    // Enforced here and not only by the pages that host the picker: hiding UI is cosmetic.
    private async Task<string> CurrentUserIdAsync()
    {
        var user = (await authenticationState.GetAuthenticationStateAsync()).User;
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Sign in to pick attributes.");
    }
}
