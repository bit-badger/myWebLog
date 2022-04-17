using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyWebLog.Features.Categories;

/// <summary>
/// Handle routes for categories
/// </summary>
[Route("/category")]
[Authorize]
public class CategoryController : MyWebLogController
{
    /// <inheritdoc />
    public CategoryController(WebLogDbContext db) : base(db) { }

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
