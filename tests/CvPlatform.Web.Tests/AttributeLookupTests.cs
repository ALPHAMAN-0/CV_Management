using System.Security.Claims;
using CvPlatform.Domain;
using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using CvPlatform.Web.Features.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Web.Tests;

// The tests of this class share one database, so each one searches by its own random token.
public sealed class AttributeLookupTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Search_matches_the_start_of_the_name_ignoring_case_and_not_the_middle()
    {
        var token = Token();
        var startsWithToken = await AddAttributeAsync($"{token} Height");
        await AddAttributeAsync($"Body {token} height");

        var found = await Lookup().SearchAsync($"  {token.ToUpperInvariant()} hei", null, default);

        Assert.Equal([startsWithToken], found.Select(c => c.Id));
    }

    [Fact]
    public async Task Search_can_be_narrowed_to_one_category()
    {
        var token = Token();
        var skill = await AddAttributeAsync($"{token} A", AttributeCategory.Skills, AttributeType.Number);
        await AddAttributeAsync($"{token} B", AttributeCategory.Education);

        var found = await Lookup().SearchAsync(token, AttributeCategory.Skills, default);

        Assert.Equal(
            new AttributeChoice(skill, $"{token} A", AttributeCategory.Skills, AttributeType.Number),
            Assert.Single(found));
    }

    [Fact]
    public async Task Search_returns_at_most_MaxResults_sorted_by_name()
    {
        var token = Token();
        var names = Enumerable.Range(0, AttributeLookup.MaxResults + 1).Select(i => $"{token} {i:00}").ToList();
        // Added last to first, so the order asserted below can't be the insertion order.
        foreach (var name in names.AsEnumerable().Reverse())
        {
            await AddAttributeAsync(name);
        }

        var found = await Lookup().SearchAsync(token, null, default);

        Assert.Equal(names.Take(AttributeLookup.MaxResults), found.Select(c => c.Name));
    }

    [Fact]
    public async Task Search_treats_wildcards_literally()
    {
        var token = Token();
        await AddAttributeAsync($"{token}abc");

        Assert.Empty(await Lookup().SearchAsync($"{token}_bc", null, default));
        Assert.Empty(await Lookup().SearchAsync($"{token}%c", null, default));
        Assert.Empty(await Lookup().SearchAsync("%", null, default));
    }

    // A blank prefix lists a whole category, so no other test in this class may use Certifications.
    [Fact]
    public async Task A_blank_prefix_lists_the_category_from_the_start()
    {
        var token = Token();
        await AddAttributeAsync($"{token} B", AttributeCategory.Certifications);
        await AddAttributeAsync($"{token} A", AttributeCategory.Certifications);

        var found = await Lookup().SearchAsync("  ", AttributeCategory.Certifications, default);

        Assert.Equal([$"{token} A", $"{token} B"], found.Select(c => c.Name));
    }

    // Unlike managing the library (AttributeService), picking is for everyone who is signed in:
    // candidates pick attributes for their own profile.
    [Theory]
    [InlineData(AppRoles.Candidate)]
    [InlineData(AppRoles.Recruiter)]
    [InlineData(AppRoles.Administrator)]
    public async Task Every_signed_in_role_may_search(string role)
    {
        var token = Token();
        var id = await AddAttributeAsync($"{token} A");

        var found = await Lookup(TestAuth.User(Guid.NewGuid().ToString(), role)).SearchAsync(token, null, default);

        Assert.Equal([id], found.Select(c => c.Id));
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        var anonymous = Lookup(new ClaimsPrincipal(new ClaimsIdentity()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.SearchAsync(null, null, default));
    }

    // A candidate by default: the least-privileged user who may use the picker.
    private AttributeLookup Lookup(ClaimsPrincipal? user = null) =>
        new(factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new FixedAuthenticationStateProvider(user ?? TestAuth.User(Guid.NewGuid().ToString(), AppRoles.Candidate)));

    private static string Token() => Guid.NewGuid().ToString("N");

    private async Task<int> AddAttributeAsync(
        string name, AttributeCategory category = AttributeCategory.Other, AttributeType type = AttributeType.String)
    {
        var attribute = new AttributeDefinition { Category = category, Type = type };
        attribute.Rename(name);

        await using var db = await CreateDbContextAsync();
        db.AttributeDefinitions.Add(attribute);
        await db.SaveChangesAsync();
        return attribute.Id;
    }

    private Task<AppDbContext> CreateDbContextAsync() =>
        factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
}
