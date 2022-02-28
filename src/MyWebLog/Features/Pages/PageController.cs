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
    /// <inheritdoc />
    public PageController(WebLogDbContext db) : base(db) { }

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
