using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class CategoryEtensions
{
    /// <summary>
    /// Count all categories
    /// </summary>
    /// <returns>A count of all categories</returns>
    public static async Task<int> CountAll(this DbSet<Category> db) =>
        await db.CountAsync().ConfigureAwait(false);
}
