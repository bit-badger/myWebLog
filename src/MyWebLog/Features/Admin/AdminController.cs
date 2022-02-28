using Microsoft.AspNetCore.Mvc;

namespace MyWebLog.Features.Admin;

/// <summary>
/// Controller for admin-specific displays and routes
/// </summary>
[Route("/admin")]
public class AdminController : MyWebLogController
{
    /// <inheritdoc />
    public AdminController(WebLogDbContext db) : base(db) { }

    [HttpGet("")]
    public async Task<IActionResult> Index() =>
        View(new DashboardModel(WebLog)
        {
            Posts = await Db.Posts.CountByStatus(PostStatus.Published),
            Drafts = await Db.Posts.CountByStatus(PostStatus.Draft),
            Pages = await Db.Pages.CountAll(),
            Categories = await Db.Categories.CountAll()
        });
}
