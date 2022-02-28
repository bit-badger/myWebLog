using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace MyWebLog.Features.Admin;

/// <summary>
/// View model for editing web log settings
/// </summary>
public class SettingsModel : MyWebLogModel
{
    /// <summary>
    /// The name of the web log
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Display(ResourceType = typeof(Resources), Name = "Name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// The subtitle of the web log
    /// </summary>
    [Display(ResourceType = typeof(Resources), Name = "Subtitle")]
    public string Subtitle { get; set; } = "";

    /// <summary>
    /// The default page
    /// </summary>
    [Required]
    [Display(ResourceType = typeof(Resources), Name = "DefaultPage")]
    public string DefaultPage { get; set; } = "";

    /// <summary>
    /// How many posts should appear on index pages
    /// </summary>
    [Required]
    [Display(ResourceType = typeof(Resources), Name = "PostsPerPage")]
    [Range(0, 50)]
    public byte PostsPerPage { get; set; } = 10;

    /// <summary>
    /// The time zone in which dates/times should be displayed
    /// </summary>
    [Required]
    [Display(ResourceType = typeof(Resources), Name = "TimeZone")]
    public string TimeZone { get; set; } = "";

    /// <summary>
    /// Possible values for the default page
    /// </summary>
    public IEnumerable<SelectListItem> DefaultPages { get; set; } = Enumerable.Empty<SelectListItem>();

    [Obsolete("Only used for model binding; use the WebLogDetails constructor")]
    public SettingsModel() : base(new()) { }

    /// <inheritdoc />
    public SettingsModel(WebLogDetails webLog) : base(webLog)
    {
        Name = webLog.Name;
        Subtitle = webLog.Subtitle ?? "";
        DefaultPage = webLog.DefaultPage;
        PostsPerPage = webLog.PostsPerPage;
        TimeZone = webLog.TimeZone;
    }

    /// <summary>
    /// Populate the settings object from the data in this form
    /// </summary>
    /// <param name="settings">The settings to be updated</param>
    public void PopulateSettings(WebLogDetails settings)
    {
        settings.Name = Name;
        settings.Subtitle = Subtitle == "" ? null : Subtitle;
        settings.DefaultPage = DefaultPage;
        settings.PostsPerPage = PostsPerPage;
        settings.TimeZone = TimeZone;
    }
}
