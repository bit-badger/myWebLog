namespace MyWebLog.Features.Shared;

/// <summary>
/// Base model class for myWebLog views
/// </summary>
public class MyWebLogModel
{
    /// <summary>
    /// The details for the web log
    /// </summary>
    public WebLogDetails WebLog { get; init; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="webLog">The details for the web log</param>
    protected MyWebLogModel(WebLogDetails webLog)
    {
        WebLog = webLog;
    }
}
