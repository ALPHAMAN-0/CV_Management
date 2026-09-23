using Microsoft.AspNetCore.Identity;

namespace CvPlatform.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    // Culture name ("en", "bn"); null = not chosen yet.
    public string? PreferredCulture { get; set; }

    // "light" or "dark"; null = not chosen yet.
    public string? PreferredTheme { get; set; }

    // Filled by the database default on insert.
    public DateTimeOffset CreatedAt { get; set; }
}
