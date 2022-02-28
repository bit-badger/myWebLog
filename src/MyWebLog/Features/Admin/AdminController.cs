using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MyWebLog.Features.Admin;

/// <summary>
/// Controller for admin-specific displays and routes
/// </summary>
[Route("/admin")]
[Authorize]
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
            ListedPages = await Db.Pages.CountListed(),
            Categories = await Db.Categories.CountAll(),
            TopLevelCategories = await Db.Categories.CountTopLevel()
        });

    [HttpGet("settings")]
    public async Task<IActionResult> Settings() =>
        View(new SettingsModel(WebLog)
        {
            DefaultPages = Enumerable.Repeat(new SelectListItem($"- {Resources.FirstPageOfPosts} -", "posts"), 1)
                .Concat((await Db.Pages.FindAll()).Select(p => new SelectListItem(p.Title, p.Id)))
        });

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(SettingsModel model)
    {
        var details = await Db.WebLogDetails.GetByHost(WebLog.UrlBase);
        if (details is null) return NotFound();

        model.PopulateSettings(details);
        await Db.SaveChangesAsync();

        // Update cache
        WebLogCache.Set(WebLogCache.HostToDb(HttpContext), (await Db.WebLogDetails.FindByHost(WebLog.UrlBase))!);
        
        // TODO: confirmation message

        return RedirectToAction(nameof(Index));
    }
}
