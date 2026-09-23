using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Web.Tests;

public sealed class UserOnboardingTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Roles_are_seeded_at_startup()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            Assert.True(await roles.RoleExistsAsync(role), role);
        }
    }

    [Fact]
    public async Task New_user_gets_the_candidate_role_and_a_profile()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users, UniqueEmail());

        await scope.ServiceProvider.GetRequiredService<UserOnboarding>().EnsureSetUpAsync(user, default);

        Assert.True(await users.IsInRoleAsync(user, AppRoles.Candidate));
        Assert.False(await users.IsInRoleAsync(user, AppRoles.Administrator));
        Assert.Equal(1, await CountProfilesAsync(user.Id));
    }

    [Fact]
    public async Task Setup_is_idempotent()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var onboarding = scope.ServiceProvider.GetRequiredService<UserOnboarding>();
        var user = await CreateUserAsync(users, UniqueEmail());

        await onboarding.EnsureSetUpAsync(user, default);
        await onboarding.EnsureSetUpAsync(user, default);

        Assert.Equal(1, await CountProfilesAsync(user.Id));
        Assert.Single(await users.GetRolesAsync(user));
    }

    // The only test in this class that creates administrators, so "none exists yet" holds at its start.
    [Fact]
    public async Task Bootstrap_email_becomes_administrator_only_while_none_exists()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var onboarding = scope.ServiceProvider.GetRequiredService<UserOnboarding>();
        var first = await CreateUserAsync(users, PostgresWebAppFactory.BootstrapAdminEmail);
        var second = await CreateUserAsync(users, PostgresWebAppFactory.SecondBootstrapEmail);

        await onboarding.EnsureSetUpAsync(first, default);
        await onboarding.EnsureSetUpAsync(second, default);

        Assert.True(await users.IsInRoleAsync(first, AppRoles.Administrator));
        Assert.False(await users.IsInRoleAsync(second, AppRoles.Administrator));
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@example.test";

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string email)
    {
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private async Task<int> CountProfilesAsync(string userId)
    {
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Profiles.CountAsync(p => p.UserId == userId);
    }
}
