using System.Diagnostics.CodeAnalysis;

namespace CvPlatform.Domain;

// Persisted by member name, so renaming a member needs a data migration.
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "These are the spec's attribute type names, not CLR types.")]
public enum AttributeType
{
    String,
    Text,
    Number,
    Date,
    Period,
    Boolean,
    Dropdown,
    Image,
}
