using CvPlatform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Web.Data.Configurations;

internal sealed class UserAttributeUsageConfiguration : IEntityTypeConfiguration<UserAttributeUsage>
{
    public void Configure(EntityTypeBuilder<UserAttributeUsage> builder)
    {
        // The key is the pair, so picking the same attribute again can only move its time.
        builder.HasKey(u => new { u.UserId, u.AttributeDefinitionId });

        // "Recently used" reads one user's rows newest first; this index hands them over in that order.
        builder.HasIndex(u => new { u.UserId, u.LastUsedAt }).IsDescending(false, true);

        // Domain has no reference to ApplicationUser, so the relationship is declared from here.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascades in the database, because attributes are deleted set-based (ExecuteDelete).
        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(u => u.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
