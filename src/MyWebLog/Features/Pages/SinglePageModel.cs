namespace MyWebLog.Features.Pages;

/// <summary>
/// The model used to render a single page
/// </summary>
public class SinglePageModel : MyWebLogModel
{
    /// <summary>
    /// The page to be rendered
    /// </summary>
    public Page Page { get; init; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="page">The page to be rendered</param>
    /// <param name="webLog">The details for the web log</param>
    public SinglePageModel(Page page, WebLogDetails webLog) : base(webLog)
    {
        Page = page;
    }
}
