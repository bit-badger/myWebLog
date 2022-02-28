using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class CategoryExtensions
{
    /// <summary>
    /// Count all categories
    /// </summary>
    /// <returns>A count of all categories</returns>
    public static async Task<int> CountAll(this DbSet<Category> db) =>
        await db.CountAsync().ConfigureAwait(false);

    /// <summary>
    /// Count top-level categories (those that do not have a parent)
    /// </summary>
    /// <returns>A count of all top-level categories</returns>
    public static async Task<int> CountTopLevel(this DbSet<Category> db) =>
        await db.CountAsync(c => c.ParentId == null).ConfigureAwait(false);
}
