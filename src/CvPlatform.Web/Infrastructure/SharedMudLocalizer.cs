using Microsoft.Extensions.Localization;
using MudBlazor;

namespace CvPlatform.Web.Infrastructure;

// Gives MudBlazor's built-in texts (pager, column menu, checkbox labels) our resource files.
// MudBlazor keeps its own English for "en"; for other cultures it asks this localizer and falls
// back to English for any key missing from SharedResource.<culture>.resx.
internal sealed class SharedMudLocalizer(IStringLocalizer<SharedResource> localizer) : MudLocalizer
{
    public override LocalizedString this[string key] => localizer[key];
}
