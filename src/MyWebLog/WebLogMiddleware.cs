using System.Collections.Concurrent;

namespace MyWebLog;

/// <summary>
/// In-memory cache of web log details
/// </summary>
/// <remarks>This is filled by the middleware via the first request for each host, and can be updated via the web log
/// settings update page</remarks>
public static class WebLogCache
{
    /// <summary>
    /// The cache of web log details
    /// </summary>
    private static readonly ConcurrentDictionary<string, WebLogDetails> _cache = new();

    /// <summary>
    /// Transform a hostname to a database name
    /// </summary>
    /// <param name="ctx">The current HTTP context</param>
    /// <returns>The hostname, with an underscore replacing a colon</returns>
    public static string HostToDb(HttpContext ctx) => ctx.Request.Host.ToUriComponent().Replace(':', '_');

    /// <summary>
    /// Does a host exist in the cache?
    /// </summary>
    /// <param name="host">The host in question</param>
    /// <returns>True if it exists, false if not</returns>
    public static bool Exists(string host) => _cache.ContainsKey(host);

    /// <summary>
    /// Get the details for a web log via its host
    /// </summary>
    /// <param name="host">The host which should be retrieved</param>
    /// <returns>The web log details</returns>
    public static WebLogDetails Get(string host) => _cache[host];

    /// <summary>
    /// Get the details for a web log via its host
    /// </summary>
    /// <param name="ctx">The HTTP context for the request</param>
    /// <returns>The web log details</returns>
    public static WebLogDetails Get(HttpContext ctx) => _cache[HostToDb(ctx)];

    /// <summary>
    /// Set the details for a particular host
    /// </summary>
    /// <param name="host">The host for which details should be set</param>
    /// <param name="details">The details to be set</param>
    public static void Set(string host, WebLogDetails details) => _cache[host] = details;
}

/// <summary>
/// Middleware to derive the current web log
/// </summary>
public class WebLogMiddleware
{
    /// <summary>
    /// The next action in the pipeline
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="next">The next action in the pipeline</param>
    public WebLogMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var host = WebLogCache.HostToDb(context);

        if (WebLogCache.Exists(host)) return;

        var db = context.RequestServices.GetRequiredService<WebLogDbContext>();
        var details = await db.WebLogDetails.FindByHost(context.Request.Host.ToUriComponent());
        if (details == null)
        {
            context.Response.StatusCode = 404;
            return;
        }
        
        WebLogCache.Set(host, details);

        await _next.Invoke(context);
    }
}
