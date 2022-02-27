using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class PageExtensions
{
    /// <summary>
    /// Retrieve a page by its ID (non-tracked)
    /// </summary>
    /// <param name="id">The ID of the page to retrieve</param>
    /// <returns>The requested page (or null if it is not found)</returns>
    public static async Task<Page?> FindById(this DbSet<Page> db, string id) =>
        await db.FirstOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);
}
