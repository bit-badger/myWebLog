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
    public IActionResult Index()
    {
        return View();
    }
}
