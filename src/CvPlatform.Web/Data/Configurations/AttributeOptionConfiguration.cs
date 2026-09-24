using CvPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Web.Data.Configurations;

internal sealed class AttributeOptionConfiguration : IEntityTypeConfiguration<AttributeOption>
{
    public void Configure(EntityTypeBuilder<AttributeOption> builder)
    {
        // No unique index on (attribute, SortOrder) or (attribute, Label): a reorder or a label swap
        // updates rows one at a time and would collide with itself halfway. Validation keeps labels unique.
        builder.Property(o => o.Label).HasMaxLength(AttributeValidation.LabelMaxLength);
    }
}
