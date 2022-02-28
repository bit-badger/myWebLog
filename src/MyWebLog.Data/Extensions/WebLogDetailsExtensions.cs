using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class WebLogDetailsExtensions
{
    /// <summary>
    /// Find the details of a web log by its host (non-tracked)
    /// </summary>
    /// <param name="host">The host</param>
    /// <returns>The web log (or null if not found)</returns>
    public static async Task<WebLogDetails?> FindByHost(this DbSet<WebLogDetails> db, string host) =>
        await db.FirstOrDefaultAsync(wld => wld.UrlBase == host).ConfigureAwait(false);

    /// <summary>
    /// Get the details of a web log by its host (tracked)
    /// </summary>
    /// <param name="host">The host</param>
    /// <returns>The web log (or null if not found)</returns>
    public static async Task<WebLogDetails?> GetByHost(this DbSet<WebLogDetails> db, string host) =>
        await db.AsTracking().FirstOrDefaultAsync(wld => wld.UrlBase == host).ConfigureAwait(false);
}
