using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CvPlatform.Web.Tests;

public sealed partial class PreferenceEndpointTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Choosing_bengali_sets_the_culture_cookie_and_translates_the_ui()
    {
        using var client = CreateClient();

        using var response = await PostAsync(client, "/Preferences/Culture", ("culture", "bn"), ("returnUrl", "Account/Login"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        // Decoded because the HTML encoder writes non-Latin text as character references.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative)));
        Assert.Contains("lang=\"bn\"", page, StringComparison.Ordinal);
        Assert.Contains("সাইন ইন", page, StringComparison.Ordinal); // "Sign in": proves the resx is found
    }

    [Fact]
    public async Task Return_url_must_stay_on_this_site()
    {
        using var client = CreateClient();

        using var response = await PostAsync(client, "/Preferences/Culture", ("culture", "en"), ("returnUrl", "/evil.example"));

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unsupported_culture_is_rejected()
    {
        using var client = CreateClient();

        using var response = await PostAsync(client, "/Preferences/Culture", ("culture", "xx"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Choosing_dark_theme_makes_the_toggle_offer_light()
    {
        using var client = CreateClient();

        using var response = await PostAsync(client, "/Preferences/Theme", ("theme", "dark"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        Assert.Contains("name=\"theme\" value=\"light\"", page, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, params (string Key, string Value)[] fields)
    {
        // Every static page carries a token in its app-bar forms; the client keeps the matching cookie.
        var page = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        var token = TokenPattern().Match(page).Groups[1].Value;

        using var content = new FormUrlEncodedContent(fields
            .Select(field => KeyValuePair.Create(field.Key, field.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", token)));
        return await client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
