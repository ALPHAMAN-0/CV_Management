using CvPlatform.Domain;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Data.Seed;

// Seeded at startup, not with HasData: system attributes are library data that admins may edit
// (name, description, category). With HasData the migrations would own those values, and any later
// tweak to the seed would generate an UpdateData that overwrites their edits. Rows are matched by
// SystemKey because ids differ per database and names are editable.
public static class SystemAttributes
{
    private static readonly (SystemAttributeKey Key, string Name, AttributeCategory Category, AttributeType Type)[] Defaults =
    [
        (SystemAttributeKey.FirstName, "First name", AttributeCategory.Personal, AttributeType.String),
        (SystemAttributeKey.LastName, "Last name", AttributeCategory.Personal, AttributeType.String),
        (SystemAttributeKey.Location, "Location", AttributeCategory.Personal, AttributeType.String),
        (SystemAttributeKey.Photo, "Photo", AttributeCategory.Personal, AttributeType.Image),
    ];

    // Adds only the missing keys and never touches existing rows, so it is safe on every startup.
    public static async Task EnsureSeededAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.AttributeDefinitions
            .Where(a => a.SystemKey != null)
            .Select(a => a.SystemKey)
            .ToListAsync(ct);

        foreach (var (key, name, category, type) in Defaults.Where(d => !existing.Contains(d.Key)))
        {
            var attribute = new AttributeDefinition { SystemKey = key, Category = category, Type = type };
            attribute.Rename(name);
            db.AttributeDefinitions.Add(attribute);
        }

        await db.SaveChangesAsync(ct);
    }
}
