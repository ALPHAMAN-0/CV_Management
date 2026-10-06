using System.Security.Claims;
using CvPlatform.Domain;
using CvPlatform.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Features.Attributes;

// The read side of the attribute picker (profile, position template, rule builder). Open to every
// signed-in user, candidates included; changing the library stays with AttributeService.
public sealed class AttributeLookup(
    IDbContextFactory<AppDbContext> dbFactory,
    AuthenticationStateProvider authenticationState)
{
    // A picker shows a short list: the user narrows it by typing, never by paging.
    public const int MaxResults = 20;

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

    // Enforced here and not only by the pages that host the picker: hiding UI is cosmetic.
    private async Task<string> CurrentUserIdAsync()
    {
        var user = (await authenticationState.GetAuthenticationStateAsync()).User;
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Sign in to pick attributes.");
    }
}
