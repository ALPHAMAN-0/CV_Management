namespace CvPlatform.Domain;

public readonly record struct MergedField<T>(T Value, bool IsConflict);

// Resolves one field after an optimistic-concurrency conflict. Base = what this editor loaded,
// mine = its current input, theirs = what the server holds now. Only a field both sides changed,
// to different values, needs the user; the caller keeps "mine" until they decide.
public static class ThreeWayMerge
{
    public static MergedField<T> Field<T>(T @base, T mine, T theirs) =>
        Field(@base, mine, theirs, EqualityComparer<T>.Default.Equals);

    // For values without useful Equals, e.g. lists compared by content.
    public static MergedField<T> Field<T>(T @base, T mine, T theirs, Func<T, T, bool> equals)
    {
        if (equals(mine, @base))
        {
            return new(theirs, IsConflict: false);
        }

        if (equals(theirs, @base) || equals(mine, theirs))
        {
            return new(mine, IsConflict: false);
        }

        return new(mine, IsConflict: true);
    }
}
