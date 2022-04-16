using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class PostExtensions
{
    /// <summary>
    /// Count the posts in the given status
    /// </summary>
    /// <param name="status">The status for which posts should be counted</param>
    /// <returns>A count of the posts in the given status</returns>
    public static async Task<int> CountByStatus(this DbSet<Post> db, PostStatus status) =>
        await db.CountAsync(p => p.Status == status).ConfigureAwait(false);

    /// <summary>
    /// Retrieve a post by its permalink (non-tracked)
    /// </summary>
    /// <param name="permalink">The possible post permalink</param>
    /// <returns>The post matching the permalink, or null if none is found</returns>
    public static async Task<Post?> FindByPermalink(this DbSet<Post> db, string permalink) =>
        await db.SingleOrDefaultAsync(p => p.Id == permalink).ConfigureAwait(false);

    /// <summary>
    /// Retrieve a page of published posts (non-tracked)
    /// </summary>
    /// <param name="pageNbr">The page number to retrieve</param>
    /// <param name="postsPerPage">The number of posts per page</param>
    /// <returns>A list of posts representing the posts for the given page</returns>
    public static async Task<List<Post>> FindPageOfPublishedPosts(this DbSet<Post> db, int pageNbr, int postsPerPage) =>
        await db.Where(p => p.Status == PostStatus.Published)
            .Skip((pageNbr - 1) * postsPerPage).Take(postsPerPage)
            .ToListAsync();
}
