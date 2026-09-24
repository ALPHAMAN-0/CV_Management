namespace CvPlatform.Web.Features.Account;

// Pages, AuthorizeView and services all check these by name, so they enforce the same rule.
public static class AppPolicies
{
    public const string RecruiterOrAdmin = "RecruiterOrAdmin";
}
