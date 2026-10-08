namespace CvPlatform.Domain;

// When a user last picked an attribute: one row per (user, attribute), behind the picker's
// "Recently used" list.
public sealed class UserAttributeUsage
{
    public required string UserId { get; init; }

    public int AttributeDefinitionId { get; init; }

    public DateTimeOffset LastUsedAt { get; init; }
}
