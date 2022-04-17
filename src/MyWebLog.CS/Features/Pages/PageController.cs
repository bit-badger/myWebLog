using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyWebLog.Features.Pages;

/// <summary>
/// Handle routes for pages
/// </summary>
[Route("/page")]
[Authorize]
public class PageController : MyWebLogController
{
    /// <summary>
    /// Pipeline with most extensions enabled
    /// </summary>
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseSmartyPants().UseAdvancedExtensions().Build();

    /// <inheritdoc />
    public PageController(WebLogDbContext db) : base(db) { }

    [HttpGet("all")]
    [HttpGet("all/page/{pageNbr:int}")]
    public async Task<IActionResult> All(int? pageNbr) =>
        View(new PageListModel(await Db.Pages.FindPageOfPages(pageNbr ?? 1), WebLog));

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        if (id == "new") return View(new EditPageModel(WebLog));

        var page = await Db.Pages.FindByIdWithRevisions(id);
        if (page == null) return NotFound();

        return View(EditPageModel.CreateFromPage(page, WebLog));
    }

    [HttpPost("{id}/edit")]
    public async Task<IActionResult> Save(EditPageModel model)
    {
        var page = model.PopulatePage(model.IsNew
            ? new()
            {
                Id = WebLogDbContext.NewId(),
                AuthorId = UserId,
                PublishedOn = DateTime.UtcNow,
                Revisions = new List<PageRevision>()
            }
            : await Db.Pages.GetById(model.PageId));
        if (page == null) return NotFound();

        page.Text = model.Source == RevisionSource.Html ? model.Text : Markdown.ToHtml(model.Text, _pipeline);
        page.UpdatedOn = DateTime.UtcNow;

        if (model.IsNew) await Db.Pages.AddAsync(page);

        await Db.SaveChangesAsync();

        // TODO: confirmation

        return RedirectToAction(nameof(All));
    }
}
