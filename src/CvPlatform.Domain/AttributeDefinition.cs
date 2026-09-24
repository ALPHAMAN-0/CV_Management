namespace CvPlatform.Domain;

public sealed class AttributeDefinition
{
    public int Id { get; init; }

    // Written only through Rename, so the unique, prefix-searched NormalizedName never drifts from Name.
    public string Name { get; private set; } = "";

    public string NormalizedName { get; private set; } = "";

    public string? Description { get; set; }

    public AttributeCategory Category { get; set; }

    // Fixed at creation: stored values live in the slot of this type. Changing it = delete and recreate.
    public AttributeType Type { get; init; }

    // Non-null marks a system attribute (seeded, never deleted).
    public SystemAttributeKey? SystemKey { get; init; }

    // Optimistic-concurrency token for the attribute and its options.
    public int Version { get; set; }

    public List<AttributeOption> Options { get; } = [];

    public void Rename(string name)
    {
        Name = name.Trim();
        NormalizedName = NormalizeName(name);
    }

    // Names are unique ignoring case and surrounding spaces; lookups compare against this form.
    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}
