using System.Security.Claims;
using CvPlatform.Domain;
using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using CvPlatform.Web.Features.Attributes;
using Microsoft.AspNetCore.Identity;
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

    [Fact]
    public async Task Recent_lists_only_the_callers_picks_newest_first()
    {
        var (alice, bob) = (await AddUserAsync(), await AddUserAsync());
        var first = await AddAttributeAsync(Token());
        var second = await AddAttributeAsync(Token());
        var bobs = await AddAttributeAsync(Token());

        await Lookup(alice).MarkUsedAsync(first, default);
        await Lookup(alice).MarkUsedAsync(second, default);
        await Lookup(bob).MarkUsedAsync(bobs, default);

        Assert.Equal([second, first], (await Lookup(alice).RecentAsync(null, default)).Select(c => c.Id));
        Assert.Equal([bobs], (await Lookup(bob).RecentAsync(null, default)).Select(c => c.Id));
    }

    [Fact]
    public async Task Picking_again_moves_the_attribute_to_the_top_without_a_second_row()
    {
        var user = await AddUserAsync();
        var first = await AddAttributeAsync(Token());
        var second = await AddAttributeAsync(Token());

        await Lookup(user).MarkUsedAsync(first, default);
        await Lookup(user).MarkUsedAsync(second, default);
        await Lookup(user).MarkUsedAsync(first, default);

        Assert.Equal([first, second], (await Lookup(user).RecentAsync(null, default)).Select(c => c.Id));
        Assert.Equal(2, await CountUsagesAsync(user));
    }

    [Fact]
    public async Task Recent_can_be_narrowed_to_one_category()
    {
        var user = await AddUserAsync();
        var name = Token();
        var skill = await AddAttributeAsync(name, AttributeCategory.Skills, AttributeType.Number);
        var education = await AddAttributeAsync(Token(), AttributeCategory.Education);
        await Lookup(user).MarkUsedAsync(skill, default);
        await Lookup(user).MarkUsedAsync(education, default);

        var recent = await Lookup(user).RecentAsync(AttributeCategory.Skills, default);

        Assert.Equal(
            new AttributeChoice(skill, name, AttributeCategory.Skills, AttributeType.Number),
            Assert.Single(recent));
    }

    [Fact]
    public async Task Recent_returns_at_most_MaxRecent_newest_first()
    {
        var user = await AddUserAsync();
        var picked = new List<int>();
        for (var i = 0; i <= AttributeLookup.MaxRecent; i++)
        {
            picked.Add(await AddAttributeAsync(Token()));
            await Lookup(user).MarkUsedAsync(picked[i], default);
        }

        var recent = await Lookup(user).RecentAsync(null, default);

        // One pick more than fits: the oldest is the one left out.
        Assert.Equal(picked.Skip(1).Reverse(), recent.Select(c => c.Id));
    }

    [Fact]
    public async Task Two_simultaneous_picks_of_the_same_attribute_leave_one_row()
    {
        var user = await AddUserAsync();
        var attribute = await AddAttributeAsync(Token());

        await Task.WhenAll(
            Lookup(user).MarkUsedAsync(attribute, default),
            Lookup(user).MarkUsedAsync(attribute, default));

        Assert.Equal(1, await CountUsagesAsync(user));
    }

    [Fact]
    public async Task Picking_an_attribute_that_no_longer_exists_is_ignored()
    {
        var user = await AddUserAsync();

        await Lookup(user).MarkUsedAsync(int.MaxValue, default);

        Assert.Empty(await Lookup(user).RecentAsync(null, default));
    }

    [Fact]
    public async Task Deleting_an_attribute_removes_it_from_recently_used()
    {
        var user = await AddUserAsync();
        var kept = await AddAttributeAsync(Token());
        var deleted = await AddAttributeAsync(Token());
        await Lookup(user).MarkUsedAsync(kept, default);
        await Lookup(user).MarkUsedAsync(deleted, default);

        // Set-based, as AttributeService deletes: only the database's own cascade can remove the usage row.
        await using (var db = await CreateDbContextAsync())
        {
            await db.AttributeDefinitions.Where(a => a.Id == deleted).ExecuteDeleteAsync();
        }

        Assert.Equal([kept], (await Lookup(user).RecentAsync(null, default)).Select(c => c.Id));
        Assert.Equal(1, await CountUsagesAsync(user));
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_usage_rows()
    {
        var user = await AddUserAsync();
        await Lookup(user).MarkUsedAsync(await AddAttributeAsync(Token()), default);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(user))!)).Succeeded);
        }

        Assert.Equal(0, await CountUsagesAsync(user));
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.RecentAsync(null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.MarkUsedAsync(1, default));
    }

    // A candidate by default: the least-privileged user who may use the picker.
    private AttributeLookup Lookup(ClaimsPrincipal? user = null) =>
        new(factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new FixedAuthenticationStateProvider(user ?? TestAuth.User(Guid.NewGuid().ToString(), AppRoles.Candidate)));

    private AttributeLookup Lookup(string userId) => Lookup(TestAuth.User(userId, AppRoles.Candidate));

    private static string Token() => Guid.NewGuid().ToString("N");

    // A real user row, because usage rows reference it. Returns the user's id.
    private async Task<string> AddUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Token()}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        return user.Id;
    }

    private async Task<int> CountUsagesAsync(string userId)
    {
        await using var db = await CreateDbContextAsync();
        return await db.UserAttributeUsages.CountAsync(u => u.UserId == userId);
    }

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
