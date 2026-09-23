using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace CvPlatform.Web.Tests;

public sealed class PostgresWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string BootstrapAdminEmail = "first-admin@example.test";
    public const string SecondBootstrapEmail = "second-admin@example.test";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseSetting("ConnectionStrings:DefaultConnection", postgres.GetConnectionString())
            .UseSetting("Admin:BootstrapEmails:0", BootstrapAdminEmail)
            .UseSetting("Admin:BootstrapEmails:1", SecondBootstrapEmail);

    public Task InitializeAsync() => postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await postgres.DisposeAsync();
    }
}
