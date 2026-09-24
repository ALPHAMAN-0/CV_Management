namespace CvPlatform.Domain.Tests;

public sealed class AttributeValidationTests
{
    [Fact]
    public void Valid_attribute_without_options_has_no_errors()
    {
        Assert.Empty(AttributeValidation.Validate("Skill level", "How good you are.", AttributeType.Number, []));
    }

    [Fact]
    public void Valid_dropdown_has_no_errors()
    {
        Assert.Empty(AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, ["Junior", "Senior"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        var errors = AttributeValidation.Validate(name, null, AttributeType.String, []);

        Assert.Equal("Name is required.", Assert.Single(errors, e => e.Key == "Name").Value);
    }

    [Fact]
    public void Name_length_is_measured_after_trimming()
    {
        var longest = new string('a', AttributeValidation.NameMaxLength);

        Assert.Empty(AttributeValidation.Validate($"  {longest}  ", null, AttributeType.String, []));
        Assert.Equal(["Name"], AttributeValidation.Validate(longest + "a", null, AttributeType.String, []).Keys);
    }

    [Fact]
    public void Description_is_optional_and_limited_in_length_after_trimming()
    {
        var longest = new string('a', AttributeValidation.DescriptionMaxLength);

        Assert.Empty(AttributeValidation.Validate("Name", "   ", AttributeType.String, []));
        Assert.Empty(AttributeValidation.Validate("Name", $" {longest} ", AttributeType.String, []));
        Assert.Equal(
            ["Description"], AttributeValidation.Validate("Name", longest + "a", AttributeType.String, []).Keys);
    }

    [Fact]
    public void Dropdown_needs_at_least_one_option()
    {
        Assert.Equal(["Options"], AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, []).Keys);
    }

    [Fact]
    public void Dropdown_options_are_limited_in_number()
    {
        var labels = Enumerable.Range(1, AttributeValidation.MaxOptions).Select(i => $"Option {i}").ToList();

        Assert.Empty(AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, labels));
        Assert.Equal(
            ["Options"],
            AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, [.. labels, "One more"]).Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Every_option_needs_a_label(string label)
    {
        var errors = AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, ["Junior", label]);

        Assert.Equal(["Options"], errors.Keys);
    }

    [Fact]
    public void Option_labels_are_limited_in_length_after_trimming()
    {
        var longest = new string('a', AttributeValidation.LabelMaxLength);

        Assert.Empty(AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, [$" {longest} "]));
        Assert.Equal(
            ["Options"], AttributeValidation.Validate("Seniority", null, AttributeType.Dropdown, [longest + "a"]).Keys);
    }

    [Fact]
    public void Option_labels_must_be_unique_ignoring_case_and_surrounding_spaces()
    {
        var errors = AttributeValidation.Validate("Colour", null, AttributeType.Dropdown, ["Red", " red "]);

        Assert.Equal("Option labels must be unique.", Assert.Single(errors, e => e.Key == "Options").Value);
    }

    [Fact]
    public void Types_without_options_accept_none()
    {
        Assert.Equal(["Options"], AttributeValidation.Validate("Nickname", null, AttributeType.String, ["Red"]).Keys);
    }

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var errors = AttributeValidation.Validate(
            "", new string('a', AttributeValidation.DescriptionMaxLength + 1), AttributeType.Dropdown, []);

        Assert.Equal(["Description", "Name", "Options"], errors.Keys.Order());
    }
}
