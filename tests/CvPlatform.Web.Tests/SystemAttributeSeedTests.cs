using CvPlatform.Domain;
using CvPlatform.Web.Data;
using CvPlatform.Web.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Web.Tests;

public sealed class SystemAttributeSeedTests(PostgresWebAppFactory factory) : IClassFixture<PostgresWebAppFactory>
{
    [Fact]
    public async Task System_attributes_are_seeded_at_startup()
    {
        var seeded = await LoadSystemAttributesAsync();

        Assert.Equal(
            [
                (SystemAttributeKey.FirstName, "First name", "FIRST NAME", AttributeCategory.Personal, AttributeType.String),
                (SystemAttributeKey.LastName, "Last name", "LAST NAME", AttributeCategory.Personal, AttributeType.String),
                (SystemAttributeKey.Location, "Location", "LOCATION", AttributeCategory.Personal, AttributeType.String),
                (SystemAttributeKey.Photo, "Photo", "PHOTO", AttributeCategory.Personal, AttributeType.Image),
            ],
            seeded.Select(a => (a.SystemKey!.Value, a.Name, a.NormalizedName, a.Category, a.Type)));
        Assert.All(seeded, a => Assert.Equal(0, a.Version));
    }

    [Fact]
    public async Task Seeding_again_adds_nothing()
    {
        var before = await LoadSystemAttributesAsync();

        await using (var db = await CreateDbContextAsync())
        {
            await SystemAttributes.EnsureSeededAsync(db, default);
        }

        Assert.Equal(before.Select(a => a.Id), (await LoadSystemAttributesAsync()).Select(a => a.Id));
    }

    // Ordered in memory: the key column stores names, so SQL would sort alphabetically, not by enum value.
    private async Task<List<AttributeDefinition>> LoadSystemAttributesAsync()
    {
        await using var db = await CreateDbContextAsync();
        var attributes = await db.AttributeDefinitions.AsNoTracking().Where(a => a.SystemKey != null).ToListAsync();
        return [.. attributes.OrderBy(a => a.SystemKey)];
    }

    private Task<AppDbContext> CreateDbContextAsync() =>
        factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
}
