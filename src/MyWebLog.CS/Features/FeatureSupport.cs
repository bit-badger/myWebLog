using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;
using System.Collections.Concurrent;
using System.Reflection;

namespace MyWebLog.Features;

/// <summary>
/// A controller model convention that identifies the feature in which a controller exists
/// </summary>
public class FeatureControllerModelConvention : IControllerModelConvention
{
    /// <summary>
    /// A cache of controller types to features
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _features = new();

    /// <summary>
    /// Derive the feature name from the controller's type
    /// </summary>
    private static string? GetFeatureName(TypeInfo typ)
    {
        var cacheKey = typ.FullName ?? "";
        if (_features.ContainsKey(cacheKey)) return _features[cacheKey];

        var tokens = cacheKey.Split('.');
        if (tokens.Any(it => it == "Features"))
        {
            var feature = tokens.SkipWhile(it => it != "Features").Skip(1).Take(1).FirstOrDefault();
            if (feature is not null)
            {
                _features[cacheKey] = feature;
                return feature;
            }
        }
        return null;
    }

    /// <inheritdoc />
    public void Apply(ControllerModel controller) =>
        controller.Properties.Add("feature", GetFeatureName(controller.ControllerType));

}

/// <summary>
/// Expand the location token with the feature name
/// </summary>
public class FeatureViewLocationExpander : IViewLocationExpander
{
    /// <inheritdoc />
    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        _ = viewLocations ?? throw new ArgumentNullException(nameof(viewLocations));
        if (context.ActionContext.ActionDescriptor is not ControllerActionDescriptor descriptor)
            throw new ArgumentException("ActionDescriptor not found");

        var feature = descriptor.Properties["feature"] as string ?? "";
        foreach (var location in viewLocations)
            yield return location.Replace("{2}", feature);
    }

    /// <inheritdoc />
    public void PopulateValues(ViewLocationExpanderContext _) { }
}
