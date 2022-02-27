using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class PostExtensions
{
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
