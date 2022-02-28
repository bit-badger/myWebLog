using Microsoft.AspNetCore.Mvc;

namespace MyWebLog.Features.Shared;

public abstract class MyWebLogController : Controller
{
    /// <summary>
    /// The data context to use to fulfil this request
    /// </summary>
    protected WebLogDbContext Db { get; init; }

    /// <summary>
    /// The details for the current web log
    /// </summary>
    protected WebLogDetails WebLog => WebLogCache.Get(HttpContext);

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="db">The data context to use to fulfil this request</param>
    protected MyWebLogController(WebLogDbContext db) : base()
    {
        Db = db;
    }

    protected ViewResult ThemedView(string template, object model)
    {
        return View(template, model);
    }
}
