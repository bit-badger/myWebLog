using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MyWebLog.Features.Shared.TagHelpers;

/// <summary>
/// Write a Yes or No based on a boolean value
/// </summary>
public class YesNoTagHelper : TagHelper
{
    /// <summary>
    /// The attribute in question
    /// </summary>
    [HtmlAttributeName("asp-for")]
    public bool For { get; set; } = false;

    /// <summary>
    /// Optional; if set, that value will be wrapped with &lt;strong&gt; instead of &lt;span&gt;
    /// </summary>
    [HtmlAttributeName("asp-strong-if")]
    public bool? StrongIf { get; set; }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = For == StrongIf ? "strong" : "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.Append(For ? Resources.Yes : Resources.No);
    }
}
