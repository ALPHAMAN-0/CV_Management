namespace CvPlatform.Domain.Tests;

public sealed class AttributeTypeCatalogTests
{
    // A new enum member without a catalog entry would fail at runtime; catch it here instead.
    [Fact]
    public void Every_attribute_type_has_exactly_one_entry()
    {
        Assert.Equal(
            Enum.GetValues<AttributeType>().Order(),
            AttributeTypeCatalog.All.Select(info => info.Type).Order());

        foreach (var type in Enum.GetValues<AttributeType>())
        {
            Assert.Equal(type, AttributeTypeCatalog.For(type).Type);
        }
    }

    [Fact]
    public void Only_dropdown_has_options()
    {
        var withOptions = AttributeTypeCatalog.All.Where(info => info.HasOptions).Select(info => info.Type);

        Assert.Equal([AttributeType.Dropdown], withOptions);
    }

    [Fact]
    public void Undefined_type_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AttributeTypeCatalog.For((AttributeType)999));
    }
}
