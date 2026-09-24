namespace CvPlatform.Domain;

// Outcome of a versioned save. Expected failures are values the caller switches over, not exceptions.
public abstract record SaveResult<T>
{
    // Only the nested cases below derive from it.
    private SaveResult()
    {
    }

    public sealed record Saved(int NewVersion, T Value) : SaveResult<T>;

    public sealed record Conflict(T ServerState) : SaveResult<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string> Errors) : SaveResult<T>;

    public sealed record Forbidden : SaveResult<T>;

    public sealed record NotFound : SaveResult<T>;
}
