using System.Security.Claims;
using CvPlatform.Web.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Infrastructure;

// Language and theme live in cookies because only the first HTTP request of a page load can read
// them (App.razor); the circuit can't. Signed-in users also get them saved, and RestoreCookies
// brings them back at sign-in, so the choice follows the user to other browsers.
public static class Preferences
{
    public const string ThemeCookie = "theme";
    public const string Light = "light";
    public const string Dark = "dark";

    public static IReadOnlyList<string> Cultures { get; } = ["en", "bn"];

    private static readonly string[] Themes = [Light, Dark];

    public static bool IsDark(HttpRequest request) => request.Cookies[ThemeCookie] == Dark;

    public static void MapPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Preferences");

        // POST with form binding: minimal APIs then require an antiforgery token, so another site
        // can't change what a signed-in user has saved.
        group.MapPost("/Culture", async (
            HttpContext context,
            IDbContextFactory<AppDbContext> dbFactory,
            [FromForm] string culture,
            [FromForm] string? returnUrl,
            CancellationToken ct) =>
        {
            if (!Cultures.Contains(culture))
            {
                return Results.BadRequest();
            }

            AppendCultureCookie(context, culture);
            if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredCulture, culture), ct);
            }
            return Back(returnUrl);
        });

        group.MapPost("/Theme", async (
            HttpContext context,
            IDbContextFactory<AppDbContext> dbFactory,
            [FromForm] string theme,
            [FromForm] string? returnUrl,
            CancellationToken ct) =>
        {
            if (!Themes.Contains(theme))
            {
                return Results.BadRequest();
            }

            context.Response.Cookies.Append(ThemeCookie, theme, CookieOptions(context));
            if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredTheme, theme), ct);
            }
            return Back(returnUrl);
        });
    }

    // At sign-in the user's saved choices win over whatever this browser had.
    public static void RestoreCookies(HttpContext context, ApplicationUser user)
    {
        if (user.PreferredCulture is { } culture && Cultures.Contains(culture))
        {
            AppendCultureCookie(context, culture);
        }
        if (user.PreferredTheme is { } theme && Themes.Contains(theme))
        {
            context.Response.Cookies.Append(ThemeCookie, theme, CookieOptions(context));
        }
    }

    private static void AppendCultureCookie(HttpContext context, string culture) =>
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            CookieOptions(context));

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        Expires = DateTimeOffset.UtcNow.AddYears(1),
        IsEssential = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
    };

    // Local targets only: redirecting to a URL taken from the form would otherwise be an open redirect.
    private static IResult Back(string? returnUrl)
    {
        var target = "~/" + returnUrl;
        return Results.LocalRedirect(RedirectHttpResult.IsLocalUrl(target) ? target : "~/");
    }
}
