using CvPlatform.Domain;

namespace CvPlatform.Web.Features.Attributes;

// Applies the user's "Keep mine" / "Use theirs" choice to a merged field. Split out of
// AttributeEditor so the options case below can be unit-tested without a running component.
public static class AttributeConflictResolution
{
    public static T Resolve<T>(MergedField<T> field, T theirs, bool keepMine) =>
        field.IsConflict && !keepMine ? theirs : field.Value;

    // "Keep mine" on the options field can still name an id the other editor deleted; resending it
    // would fail with "Unknown option." A stale id becomes a new option instead, so the option itself
    // survives the retry even though its old row is gone.
    public static IReadOnlyList<AttributeOptionInput> ResolveOptions(
        MergedField<IReadOnlyList<AttributeOptionInput>> field, IReadOnlyList<AttributeOptionInput> theirs, bool keepMine)
    {
        if (!field.IsConflict)
        {
            return field.Value;
        }
        if (!keepMine)
        {
            return theirs;
        }
        var theirIds = theirs.Select(o => o.Id).ToHashSet();
        return [.. field.Value.Select(o => theirIds.Contains(o.Id) ? o : o with { Id = 0 })];
    }
}
