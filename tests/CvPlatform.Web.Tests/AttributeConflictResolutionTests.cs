using CvPlatform.Domain;
using CvPlatform.Web.Features.Attributes;

namespace CvPlatform.Web.Tests;

public sealed class AttributeConflictResolutionTests
{
    [Fact]
    public void Resolve_returns_the_field_value_when_there_is_no_conflict()
    {
        var field = new MergedField<string>("theirs", IsConflict: false);

        Assert.Equal("theirs", AttributeConflictResolution.Resolve(field, "theirs", keepMine: false));
    }

    [Fact]
    public void Resolve_keeps_mine_on_a_conflict_when_the_user_chose_it()
    {
        var field = new MergedField<string>("mine", IsConflict: true);

        Assert.Equal("mine", AttributeConflictResolution.Resolve(field, "theirs", keepMine: true));
    }

    [Fact]
    public void Resolve_takes_theirs_on_a_conflict_when_the_user_declined_mine()
    {
        var field = new MergedField<string>("mine", IsConflict: true);

        Assert.Equal("theirs", AttributeConflictResolution.Resolve(field, "theirs", keepMine: false));
    }

    [Fact]
    public void ResolveOptions_returns_the_field_value_unchanged_when_there_is_no_conflict()
    {
        IReadOnlyList<AttributeOptionInput> mine = [new(1, "Red")];
        var field = new MergedField<IReadOnlyList<AttributeOptionInput>>(mine, IsConflict: false);

        var resolved = AttributeConflictResolution.ResolveOptions(field, theirs: [], keepMine: true);

        Assert.Same(mine, resolved);
    }

    [Fact]
    public void ResolveOptions_takes_theirs_on_a_conflict_when_the_user_declined_mine()
    {
        IReadOnlyList<AttributeOptionInput> mine = [new(1, "Crimson")];
        IReadOnlyList<AttributeOptionInput> theirs = [new(1, "Red")];
        var field = new MergedField<IReadOnlyList<AttributeOptionInput>>(mine, IsConflict: true);

        var resolved = AttributeConflictResolution.ResolveOptions(field, theirs, keepMine: false);

        Assert.Same(theirs, resolved);
    }

    [Fact]
    public void ResolveOptions_keeps_an_option_id_the_server_still_has()
    {
        IReadOnlyList<AttributeOptionInput> mine = [new(1, "Crimson"), new(2, "Green")];
        IReadOnlyList<AttributeOptionInput> theirs = [new(1, "Red"), new(2, "Green")];
        var field = new MergedField<IReadOnlyList<AttributeOptionInput>>(mine, IsConflict: true);

        var resolved = AttributeConflictResolution.ResolveOptions(field, theirs, keepMine: true);

        Assert.Equal([new AttributeOptionInput(1, "Crimson"), new AttributeOptionInput(2, "Green")], resolved);
    }

    // Regression: Editor A deletes option 3 (Blue) and saves. Editor B, still on the old version,
    // renamed option 1 and never touched option 3, so B's list still names it. Resending id 3 as-is
    // would fail with "Unknown option." on the retry; it must become a new option instead.
    [Fact]
    public void ResolveOptions_turns_an_id_the_server_no_longer_has_into_a_new_option()
    {
        IReadOnlyList<AttributeOptionInput> mine = [new(1, "Crimson"), new(2, "Green"), new(3, "Blue")];
        IReadOnlyList<AttributeOptionInput> theirs = [new(1, "Red"), new(2, "Green")];
        var field = new MergedField<IReadOnlyList<AttributeOptionInput>>(mine, IsConflict: true);

        var resolved = AttributeConflictResolution.ResolveOptions(field, theirs, keepMine: true);

        Assert.Equal(
            [new AttributeOptionInput(1, "Crimson"), new AttributeOptionInput(2, "Green"), new AttributeOptionInput(0, "Blue")],
            resolved);
    }
}
