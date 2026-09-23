namespace CvPlatform.Web.Features.Account;

// Role names end up in auth-cookie claims and are compared case-sensitively: always use these.
public static class AppRoles
{
    public const string Candidate = "Candidate";
    public const string Recruiter = "Recruiter";
    public const string Administrator = "Administrator";

    public static IReadOnlyList<string> All { get; } = [Candidate, Recruiter, Administrator];
}
