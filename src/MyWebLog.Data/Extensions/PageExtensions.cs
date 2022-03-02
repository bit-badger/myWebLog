using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class PageExtensions
{
    /// <summary>
    /// Count the number of pages
    /// </summary>
    /// <returns>The number of pages</returns>
    public static async Task<int> CountAll(this DbSet<Page> db) =>
        await db.CountAsync().ConfigureAwait(false);

    /// <summary>
    /// Count the number of pages in the page list
    /// </summary>
    /// <returns>The number of pages in the page list</returns>
    public static async Task<int> CountListed(this DbSet<Page> db) =>
        await db.CountAsync(p => p.ShowInPageList).ConfigureAwait(false);

    /// <summary>
    /// Retrieve all pages (non-tracked)
    /// </summary>
    /// <returns>A list of all pages</returns>
    public static async Task<List<Page>> FindAll(this DbSet<Page> db) =>
        await db.OrderBy(p => p.Title).ToListAsync().ConfigureAwait(false);

    /// <summary>
    /// Retrieve a page by its ID (non-tracked)
    /// </summary>
    /// <param name="id">The ID of the page to retrieve</param>
    /// <returns>The requested page (or null if it is not found)</returns>
    public static async Task<Page?> FindById(this DbSet<Page> db, string id) =>
        await db.SingleOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);

    /// <summary>
    /// Retrieve a page by its ID, including its revisions (non-tracked)
    /// </summary>
    /// <param name="id">The ID of the page to retrieve</param>
    /// <returns>The requested page (or null if it is not found)</returns>
    public static async Task<Page?> FindByIdWithRevisions(this DbSet<Page> db, string id) =>
        await db.Include(p => p.Revisions).SingleOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);

    /// <summary>
    /// Retrieve a page by its permalink (non-tracked)
    /// </summary>
    /// <param name="permalink">The permalink</param>
    /// <returns>The requested page (or null if it is not found)</returns>
    public static async Task<Page?> FindByPermalink(this DbSet<Page> db, string permalink) =>
        await db.SingleOrDefaultAsync(p => p.Permalink == permalink).ConfigureAwait(false);

    /// <summary>
    /// Retrieve a page of pages (non-tracked)
    /// </summary>
    /// <param name="pageNbr">The page number to retrieve</param>
    /// <returns>The pages</returns>
    public static async Task<List<Page>> FindPageOfPages(this DbSet<Page> db, int pageNbr) =>
        await db.Skip((pageNbr - 1) * 50).Take(25).ToListAsync().ConfigureAwait(false);
    
    /// <summary>
    /// Retrieve a page by its ID (tracked)
    /// </summary>
    /// <param name="id">The ID of the page to retrieve</param>
    /// <returns>The requested page (or null if it is not found)</returns>
    public static async Task<Page?> GetById(this DbSet<Page> db, string id) =>
        await db.AsTracking().Include(p => p.Revisions).SingleOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);
}
