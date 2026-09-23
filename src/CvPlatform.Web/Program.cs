using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CvPlatform.Web.Components;
using CvPlatform.Web.Components.Account;
using CvPlatform.Web.Data;
using CvPlatform.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authentication.AddIdentityCookies();
builder.Services.AddAuthorization();

// Providers register only when their keys are configured, so local runs and tests need no secrets.
var google = builder.Configuration.GetSection("Authentication:Google");
if (google.Exists())
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = google["ClientId"]!;
        options.ClientSecret = google["ClientSecret"]!;
        // Accounts are linked and admins bootstrapped by email, so only a verified one may pass.
        options.ClaimActions.Remove(ClaimTypes.Email);
        options.ClaimActions.MapCustomJson(ClaimTypes.Email, user =>
            user.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True
                ? user.GetProperty("email").GetString()
                : null);
    });
}

var gitHub = builder.Configuration.GetSection("Authentication:GitHub");
if (gitHub.Exists())
{
    // AddOAuth with our handler instead of AddGitHub: see VerifiedEmailGitHubHandler.
    authentication.AddOAuth<GitHubAuthenticationOptions, VerifiedEmailGitHubHandler>(
        GitHubAuthenticationDefaults.AuthenticationScheme,
        GitHubAuthenticationDefaults.DisplayName,
        options =>
        {
            options.ClientId = gitHub["ClientId"]!;
            options.ClientSecret = gitHub["ClientSecret"]!;
            // Lets the handler read the verified-emails list even when the address is private.
            options.Scope.Add("user:email");
            // The profile's public "email" has no verified flag; always use the verified-emails lookup.
            options.ClaimActions.Remove(ClaimTypes.Email);
        });
}

var connectionString = PostgresConnectionString.Normalize(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found."));

// A factory, not a scoped context: in Blazor Server a scope lives as long as the circuit.
// AddDbContextFactory also registers the scoped AppDbContext that Identity's stores need.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddDataProtection()
    .SetApplicationName("CvPlatform")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres");

// The host terminates TLS at its proxy; without X-Forwarded-Proto the app thinks it runs on
// http and generates wrong OAuth redirect URIs. The proxy's address isn't known up front.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Single instance: applying migrations at startup is safe and keeps deploys one step.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Explicit on purpose: left implicit, WebApplication inserts authentication before all of our
// middleware, so OAuth callbacks ran before UseForwardedHeaders, saw http:// behind the proxy
// and sent a redirect_uri the provider rejected.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

await app.RunAsync();
