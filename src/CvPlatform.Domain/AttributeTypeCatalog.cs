namespace CvPlatform.Domain;

public sealed record AttributeTypeInfo(AttributeType Type, bool HasOptions);

// The one place that knows how each attribute type behaves, so callers never switch on the type.
public static class AttributeTypeCatalog
{
    public static IReadOnlyList<AttributeTypeInfo> All { get; } =
    [
        new(AttributeType.String, HasOptions: false),
        new(AttributeType.Text, HasOptions: false),
        new(AttributeType.Number, HasOptions: false),
        new(AttributeType.Date, HasOptions: false),
        new(AttributeType.Period, HasOptions: false),
        new(AttributeType.Boolean, HasOptions: false),
        new(AttributeType.Dropdown, HasOptions: true),
        new(AttributeType.Image, HasOptions: false),
    ];

    public static AttributeTypeInfo For(AttributeType type) =>
        All.FirstOrDefault(info => info.Type == type)
        ?? throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown attribute type.");
}
