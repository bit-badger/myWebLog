using Microsoft.AspNetCore.Mvc.Razor;

namespace MyWebLog.Features;

/// <summary>
/// Expand the location token with the theme path
/// </summary>
public class ThemeViewLocationExpander : IViewLocationExpander
{
    /// <inheritdoc />
    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        _ = viewLocations ?? throw new ArgumentNullException(nameof(viewLocations));

        foreach (var location in viewLocations)
            yield return location.Replace("{3}", context.Values["theme"]!);
    }

    /// <inheritdoc />
    public void PopulateValues(ViewLocationExpanderContext context)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));

        context.Values["theme"] = WebLogCache.Get(context.ActionContext.HttpContext).ThemePath;
    }
}
