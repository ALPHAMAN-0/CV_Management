using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    // Containers are ephemeral: keys kept on disk would vanish on redeploy and log everyone out.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
