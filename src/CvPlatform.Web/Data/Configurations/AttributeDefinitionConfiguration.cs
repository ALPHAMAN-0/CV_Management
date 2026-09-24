using CvPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Web.Data.Configurations;

internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    // Longest enum member name is well below this; varchar keeps the stored names readable in SQL.
    private const int EnumMaxLength = 20;

    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(AttributeValidation.NameMaxLength);
        builder.Property(a => a.NormalizedName).HasMaxLength(AttributeValidation.NameMaxLength);

        // text_pattern_ops lets LIKE 'prefix%' use this index whatever the database collation,
        // so the name lookup is index-backed.
        builder.HasIndex(a => a.NormalizedName).IsUnique().HasOperators("text_pattern_ops");

        builder.Property(a => a.Description).HasMaxLength(AttributeValidation.DescriptionMaxLength);

        // Stored by name, not number: reordering enum members can't silently change stored data.
        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(a => a.SystemKey).HasConversion<string>().HasMaxLength(EnumMaxLength);

        // One row per system key; NULLs (ordinary attributes) are distinct, so they never collide.
        builder.HasIndex(a => a.SystemKey).IsUnique();

        builder.Property(a => a.Version).IsConcurrencyToken();

        // Options belong to the attribute: deleting it (including set-based ExecuteDelete, which
        // bypasses the change tracker) removes them in the database.
        builder.HasMany(a => a.Options)
            .WithOne()
            .HasForeignKey(o => o.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
