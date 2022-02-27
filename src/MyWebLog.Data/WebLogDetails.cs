namespace MyWebLog.Data;

/// <summary>
/// The details about a web log
/// </summary>
public class WebLogDetails
{
    /// <summary>
    /// The name of the web log
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// A subtitle for the web log
    /// </summary>
    public string? Subtitle { get; set; } = null;

    /// <summary>
    /// The default page ("posts" or a page Id)
    /// </summary>
    public string DefaultPage { get; set; } = "";

    /// <summary>
    /// The number of posts to display on pages of posts
    /// </summary>
    public byte PostsPerPage { get; set; } = 10;

    /// <summary>
    /// The path of the theme (within /views/themes)
    /// </summary>
    public string ThemePath { get; set; } = "Default";

    /// <summary>
    /// The URL base
    /// </summary>
    public string UrlBase { get; set; } = "";

    /// <summary>
    /// The time zone in which dates/times should be displayed
    /// </summary>
    public string TimeZone { get; set; } = "";
}
