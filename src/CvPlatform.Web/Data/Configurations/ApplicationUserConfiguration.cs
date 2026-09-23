using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CvPlatform.Web.Data.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.PreferredCulture).HasMaxLength(10);
        builder.Property(u => u.PreferredTheme).HasMaxLength(5);

        // Set by the database so existing rows get a value when the column is added.
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
    }
}
