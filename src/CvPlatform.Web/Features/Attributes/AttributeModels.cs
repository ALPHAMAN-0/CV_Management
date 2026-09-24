using CvPlatform.Domain;

namespace CvPlatform.Web.Features.Attributes;

// A record so rows re-fetched on every page load still equal the selected ones.
public sealed record AttributeRow(
    int Id, string Name, AttributeCategory Category, AttributeType Type, bool IsSystem, string? Description);

// Id 0 marks an option the editor added; any other Id must be one of the attribute's own options.
public sealed record AttributeOptionInput(int Id, string Label);

// The editable part of an attribute: what the editor sends, and what it merges field by field on a conflict.
public sealed record AttributeFields(
    string Name,
    string? Description,
    AttributeCategory Category,
    AttributeType Type,
    IReadOnlyList<AttributeOptionInput> Options);

// Version is the one the editor must send back with its next save.
public sealed record AttributeDetails(int Id, int Version, bool IsSystem, AttributeFields Fields);
