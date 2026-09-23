using System.Net;
using System.Security.Claims;
using CvPlatform.Web.Data;
using CvPlatform.Web.Features.Account;
using CvPlatform.Web.Features.Admin;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace CvPlatform.Web.Tests;

public sealed class UserAdminTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Lists_one_page_sorted_by_email_with_the_total_count()
    {
        var token = Guid.NewGuid().ToString("N");
        await CreateUsersAsync($"{token}-b@example.test", $"{token}-a@example.test", $"{token}-c@example.test");

        var page = await CreateService(Principal(AppRoles.Administrator))
            .ListAsync(token, State(page: 0, pageSize: 2, sortBy: nameof(UserRow.Email)), default);

        Assert.Equal(3, page.TotalItems);
        Assert.Equal([$"{token}-a@example.test", $"{token}-b@example.test"], page.Items.Select(u => u.Email));
    }

    [Fact]
    public async Task Search_matches_part_of_the_email_ignoring_case()
    {
        var token = Guid.NewGuid().ToString("N");
        await CreateUsersAsync($"{token}@example.test", $"other-{Guid.NewGuid():N}@example.test");

        var page = await CreateService(Principal(AppRoles.Administrator))
            .ListAsync(token.ToUpperInvariant(), State(page: 0, pageSize: 10), default);

        Assert.Equal($"{token}@example.test", Assert.Single(page.Items).Email);
    }

    [Fact]
    public async Task Rows_carry_role_flags()
    {
        var token = Guid.NewGuid().ToString("N");
        var recruiter = (await CreateUsersAsync($"{token}@example.test"))[0];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddToRoleAsync((await users.FindByIdAsync(recruiter.Id))!, AppRoles.Recruiter);
        }

        var page = await CreateService(Principal(AppRoles.Administrator))
            .ListAsync(token, State(page: 0, pageSize: 10), default);

        var row = Assert.Single(page.Items);
        Assert.True(row.IsRecruiter);
        Assert.False(row.IsAdministrator);
        Assert.False(row.IsBlocked);
    }

    [Fact]
    public async Task Non_administrators_are_refused()
    {
        var service = CreateService(Principal(AppRoles.Candidate, AppRoles.Recruiter));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ListAsync(null, State(page: 0, pageSize: 10), default));
    }

    // Regression test: authentication must run after UseForwardedHeaders, or everything it builds
    // behind the TLS proxy (login redirects, OAuth redirect_uri) says http://.
    [Fact]
    public async Task Anonymous_request_behind_a_tls_proxy_is_redirected_to_https_login()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/users", UriKind.Relative));
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Account/Login", response.Headers.Location?.AbsoluteUri, StringComparison.Ordinal);
    }

    private UserAdminService CreateService(ClaimsPrincipal principal) =>
        new(factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            new FixedAuthenticationStateProvider(principal));

    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(role => new Claim(ClaimTypes.Role, role)), authenticationType: "Test"));

    private static GridState<UserRow> State(int page, int pageSize, string? sortBy = null) => new()
    {
        Page = page,
        PageSize = pageSize,
        SortDefinitions = sortBy is null ? [] : [new SortDefinition<UserRow>(sortBy, false, 0, row => row.Email)],
    };

    private async Task<List<ApplicationUser>> CreateUsersAsync(params string[] emails)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = new List<ApplicationUser>();
        foreach (var email in emails)
        {
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            created.Add(user);
        }
        return created;
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
