namespace CvPlatform.Domain.Tests;

public sealed class ThreeWayMergeTests
{
    [Fact]
    public void Field_untouched_here_takes_theirs()
    {
        Assert.Equal(
            new MergedField<string>("theirs", IsConflict: false),
            ThreeWayMerge.Field("base", "base", "theirs"));
    }

    [Fact]
    public void Field_untouched_there_keeps_mine()
    {
        Assert.Equal(
            new MergedField<string>("mine", IsConflict: false),
            ThreeWayMerge.Field("base", "mine", "base"));
    }

    [Fact]
    public void Same_edit_on_both_sides_is_not_a_conflict()
    {
        Assert.Equal(
            new MergedField<string>("same", IsConflict: false),
            ThreeWayMerge.Field("base", "same", "same"));
    }

    [Fact]
    public void Different_edits_on_both_sides_conflict_and_keep_mine_until_the_user_decides()
    {
        Assert.Equal(
            new MergedField<string>("mine", IsConflict: true),
            ThreeWayMerge.Field("base", "mine", "theirs"));
    }

    [Fact]
    public void Null_is_a_value_like_any_other()
    {
        Assert.Equal(
            new MergedField<string?>("mine", IsConflict: false),
            ThreeWayMerge.Field<string?>(null, "mine", null));
        Assert.Equal(
            new MergedField<string?>(null, IsConflict: false),
            ThreeWayMerge.Field<string?>("base", "base", null));
    }

    [Fact]
    public void Lists_are_compared_by_content_with_the_given_equality()
    {
        List<string> @base = ["Red", "Green"];
        List<string> mine = ["Red", "Green"]; // a different instance with the same content: untouched
        List<string> theirs = ["Green", "Red"];

        var merged = ThreeWayMerge.Field(@base, mine, theirs, (a, b) => a.SequenceEqual(b));

        Assert.Same(theirs, merged.Value);
        Assert.False(merged.IsConflict);
    }

    [Fact]
    public void Lists_changed_differently_on_both_sides_conflict()
    {
        List<string> @base = ["Red"];
        List<string> mine = ["Red", "Blue"];
        List<string> theirs = ["Red", "Green"];

        var merged = ThreeWayMerge.Field(@base, mine, theirs, (a, b) => a.SequenceEqual(b));

        Assert.Same(mine, merged.Value);
        Assert.True(merged.IsConflict);
    }
}
