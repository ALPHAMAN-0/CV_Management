namespace CvPlatform.Web.Tests;

public sealed class HealthEndpointTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task Health_reports_healthy_once_migrations_have_run()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
