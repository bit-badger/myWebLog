using Microsoft.AspNetCore.Mvc;

namespace MyWebLog.Features.Posts;

/// <summary>
/// Handle post-related requests
/// </summary>
public class PostController : MyWebLogController
{
    /// <inheritdoc />
    public PostController(WebLogDbContext db) : base(db) { }

    [HttpGet("~/")]
    public async Task<IActionResult> Index()
    {
        var webLog = WebLogCache.Get(HttpContext);
        if (webLog.DefaultPage == "posts")
        {
            var posts = await Db.Posts.FindPageOfPublishedPosts(1, webLog.PostsPerPage);
            return ThemedView("Index", posts);
        }
        var page = await Db.Pages.FindById(webLog.DefaultPage);
        return page is null ? NotFound() : ThemedView("SinglePage", page);
    }
}
