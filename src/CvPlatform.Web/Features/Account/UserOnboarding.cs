using CvPlatform.Domain;
using CvPlatform.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Features.Account;

// Runs on every external sign-in, before the auth cookie is issued, so the cookie already carries
// the roles. Idempotent, so it also repairs accounts created before this code existed.
public sealed class UserOnboarding(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration)
{
    public async Task EnsureSetUpAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!await userManager.IsInRoleAsync(user, AppRoles.Candidate))
        {
            await AddToRoleAsync(user, AppRoles.Candidate);
        }

        // A recovery path, not a permanent grant: it applies only while nobody is Administrator,
        // so an admin who removes their own role isn't silently re-promoted on the next sign-in.
        if (IsBootstrapEmail(user.Email)
            && (await userManager.GetUsersInRoleAsync(AppRoles.Administrator)).Count == 0)
        {
            await AddToRoleAsync(user, AppRoles.Administrator);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Profiles.AnyAsync(p => p.UserId == user.Id, ct))
        {
            db.Profiles.Add(new Profile { UserId = user.Id, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
    }

    private bool IsBootstrapEmail(string? email)
    {
        var bootstrapEmails = configuration.GetSection("Admin:BootstrapEmails").Get<string[]>() ?? [];
        return email is not null && bootstrapEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
    }

    private async Task AddToRoleAsync(ApplicationUser user, string role)
    {
        var result = await userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not add role '{role}': {errors}");
        }
    }
}
