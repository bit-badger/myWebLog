using Microsoft.AspNetCore.Mvc.Razor.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;

namespace MyWebLog.Features.Shared.TagHelpers;

/// <summary>
/// Tag helper to link stylesheets for a theme
/// </summary>
[HtmlTargetElement("link", Attributes = "asp-theme")]
public class LinkTagHelper : Microsoft.AspNetCore.Mvc.TagHelpers.LinkTagHelper
{
    /// <summary>
    /// The theme for which a style sheet should be loaded
    /// </summary>
    [HtmlAttributeName("asp-theme")]
    public string Theme { get; set; } = "";

    /// <summary>
    /// The style sheet to be loaded (defaults to "style")
    /// </summary>
    [HtmlAttributeName("asp-style")]
    public string Style { get; set; } = "style";

    /// <inheritdoc />
    public LinkTagHelper(IWebHostEnvironment hostingEnvironment, TagHelperMemoryCacheProvider cacheProvider,
        IFileVersionProvider fileVersionProvider, HtmlEncoder htmlEncoder, JavaScriptEncoder javaScriptEncoder,
        IUrlHelperFactory urlHelperFactory)
        : base(hostingEnvironment, cacheProvider, fileVersionProvider, htmlEncoder, javaScriptEncoder, urlHelperFactory)
    { }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Theme == "")
        {
            base.Process(context, output);
            return;
        }

        switch (context.AllAttributes["rel"]?.Value.ToString())
        {
            case "stylesheet":
                output.Attributes.SetAttribute("href", $"~/css/{Theme}/{Style}.css");
                break;
            case "icon":
                output.Attributes.SetAttribute("type", "image/x-icon");
                output.Attributes.SetAttribute("href", $"~/img/{Theme}/favicon.ico");
                break;
        }
        ProcessUrlAttribute("href", output);
    }
}
