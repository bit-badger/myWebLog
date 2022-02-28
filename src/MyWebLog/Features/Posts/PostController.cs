using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyWebLog.Features.Pages;

namespace MyWebLog.Features.Posts;

/// <summary>
/// Handle post-related requests
/// </summary>
[Route("/post")]
[Authorize]
public class PostController : MyWebLogController
{
    /// <inheritdoc />
    public PostController(WebLogDbContext db) : base(db) { }

    [HttpGet("~/")]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        if (WebLog.DefaultPage == "posts") return await PageOfPosts(1);
        
        var page = await Db.Pages.FindById(WebLog.DefaultPage);
        return page is null ? NotFound() : ThemedView("SinglePage", new SinglePageModel(page, WebLog));
    }

    [HttpGet("~/page/{pageNbr:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> PageOfPosts(int pageNbr) =>
        ThemedView("Index",
            new MultiplePostModel(await Db.Posts.FindPageOfPublishedPosts(pageNbr, WebLog.PostsPerPage), WebLog));

    [HttpGet("~/{*permalink}")]
    public async Task<IActionResult> CatchAll(string permalink)
    {
        var post = await Db.Posts.FindByPermalink(permalink);
        if (post != null)
        {
            // TODO: return via single-post action
        }

        var page = await Db.Pages.FindByPermalink(permalink);
        if (page != null)
        {
            return ThemedView("SinglePage", new SinglePageModel(page, WebLog));
        }

        // TOOD: search prior permalinks for posts and pages

        // We tried, we really tried...
        Console.Write($"Returning 404 for permalink |{permalink}|");
        return NotFound();
    }

    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }
}
