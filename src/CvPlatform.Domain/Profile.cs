namespace CvPlatform.Domain;

// One per user (the key is the user's id), created on first sign-in. Values and projects arrive
// with the attribute library and profile phases.
public sealed class Profile
{
    public required string UserId { get; init; }

    // Optimistic-concurrency token for the whole profile aggregate.
    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
