namespace MyWebLog.Features.Pages;

/// <summary>
/// View model for viewing a list of pages
/// </summary>
public class PageListModel : MyWebLogModel
{
    public IList<Page> Pages { get; init; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="pages">The pages to display</param>
    /// <param name="webLog">The web log details</param>
    public PageListModel(IList<Page> pages, WebLogDetails webLog) : base(webLog)
    {
        Pages = pages;
    }
}
