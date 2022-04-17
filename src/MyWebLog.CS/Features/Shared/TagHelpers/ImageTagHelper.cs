using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;

namespace MyWebLog.Features.Shared.TagHelpers;

/// <summary>
/// Image tag helper to load a theme's image
/// </summary>
[HtmlTargetElement("img", Attributes = "asp-theme")]
public class ImageTagHelper : Microsoft.AspNetCore.Mvc.TagHelpers.ImageTagHelper
{
    /// <summary>
    /// The theme for which the image should be loaded
    /// </summary>
    [HtmlAttributeName("asp-theme")]
    public string Theme { get; set; } = "";

    /// <inheritdoc />
    public ImageTagHelper(IFileVersionProvider fileVersionProvider, HtmlEncoder htmlEncoder,
        IUrlHelperFactory urlHelperFactory)
        : base(fileVersionProvider, htmlEncoder, urlHelperFactory) { }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Theme == "")
        {
            base.Process(context, output);
            return;
        }

        output.Attributes.SetAttribute("src", $"~/{Theme}/img/{context.AllAttributes["src"]?.Value}");
        ProcessUrlAttribute("src", output);
    }
}
