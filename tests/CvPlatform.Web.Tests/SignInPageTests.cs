using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace CvPlatform.Web.Tests;

public sealed class SignInPageTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    // Anyone can send a link with ?RemoteError=..., so its text must never appear on our sign-in page.
    [Fact]
    public async Task Provider_error_text_from_the_url_is_not_shown()
    {
        using var client = factory.CreateClient();

        var page = await client.GetStringAsync(
            new Uri("/Account/ExternalLogin?RemoteError=Visit%20evil.example%20now", UriKind.Relative));

        Assert.DoesNotContain("evil.example", page, StringComparison.Ordinal);
        Assert.Contains("The sign-in provider reported an error.", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MudBlazor_texts_come_from_our_bengali_resources()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var localizer = scope.ServiceProvider.GetRequiredService<MudLocalizer>();
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("bn");
        try
        {
            var rowsPerPage = localizer["MudDataGridPager_RowsPerPage"];

            Assert.False(rowsPerPage.ResourceNotFound);
            Assert.Equal("প্রতি পৃষ্ঠায় সারি:", rowsPerPage.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
