namespace MyWebLog.Features.Admin;

/// <summary>
/// The model used to display the dashboard
/// </summary>
public class DashboardModel : MyWebLogModel
{
    /// <summary>
    /// The number of published posts
    /// </summary>
    public int Posts { get; set; } = 0;

    /// <summary>
    /// The number of post drafts
    /// </summary>
    public int Drafts { get; set; } = 0;

    /// <summary>
    /// The number of pages
    /// </summary>
    public int Pages { get; set; } = 0;

    /// <summary>
    /// The number of categories
    /// </summary>
    public int Categories { get; set; } = 0;

    /// <inheritdoc />
    public DashboardModel(WebLogDetails webLog) : base(webLog) { }
}
