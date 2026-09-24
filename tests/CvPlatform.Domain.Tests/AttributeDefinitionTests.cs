namespace CvPlatform.Domain.Tests;

public sealed class AttributeDefinitionTests
{
    [Theory]
    [InlineData("Skill level", "SKILL LEVEL")]
    [InlineData("  first name \t", "FIRST NAME")]
    public void NormalizeName_trims_and_upper_cases(string name, string expected)
    {
        Assert.Equal(expected, AttributeDefinition.NormalizeName(name));
    }

    [Fact]
    public void Rename_trims_the_name_and_keeps_the_normalized_name_in_step()
    {
        var attribute = new AttributeDefinition();

        attribute.Rename("  Skill level ");

        Assert.Equal("Skill level", attribute.Name);
        Assert.Equal("SKILL LEVEL", attribute.NormalizedName);
    }
}
