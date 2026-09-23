using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace CvPlatform.Web.Infrastructure;

// The stock handler takes the *primary* address from /user/emails without checking that GitHub
// verified it. We link accounts and grant the bootstrap admin role by email, so an unverified
// address would let anyone claim someone else's account.
internal sealed class VerifiedEmailGitHubHandler(
    IOptionsMonitor<GitHubAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : GitHubAuthenticationHandler(options, logger, encoder)
{
    protected override async Task<string?> GetEmailAsync(OAuthTokenResponse tokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserEmailsEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(Context.RequestAborted);
        using var payload = await JsonDocument.ParseAsync(body, cancellationToken: Context.RequestAborted);

        return payload.RootElement.EnumerateArray()
            .Where(address => address.GetProperty("primary").GetBoolean()
                && address.GetProperty("verified").GetBoolean())
            .Select(address => address.GetProperty("email").GetString())
            .FirstOrDefault();
    }
}
