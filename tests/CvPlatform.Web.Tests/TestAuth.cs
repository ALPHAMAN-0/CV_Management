using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CvPlatform.Web.Tests;

// Services read the current user from AuthenticationStateProvider; tests hand them a fixed one.
internal static class TestAuth
{
    public static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(role => new Claim(ClaimTypes.Role, role)), authenticationType: "Test"));

    // For services that act for one particular user: carries the id claim Identity puts in its cookie.
    public static ClaimsPrincipal User(string userId, params string[] roles) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
            authenticationType: "Test"));
}

internal sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(principal));
}
