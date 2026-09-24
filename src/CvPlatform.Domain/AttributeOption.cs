namespace CvPlatform.Domain;

// A choice of a Dropdown attribute; part of the attribute's aggregate, so edits bump its Version.
public sealed class AttributeOption
{
    public int Id { get; init; }

    public int AttributeDefinitionId { get; init; }

    public required string Label { get; set; }

    public int SortOrder { get; set; }
}
