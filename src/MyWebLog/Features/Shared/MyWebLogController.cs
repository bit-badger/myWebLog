using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MyWebLog.Features.Shared;

/// <summary>
/// Base class for myWebLog controllers
/// </summary>
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
    /// The ID of the currently authenticated user
    /// </summary>
    protected string UserId => User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "";

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
        // TODO: get actual version
        ViewBag.Version = "2";
        return View(template, model);
    }
}
