using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MyWebLog.Features.Users;

/// <summary>
/// Controller for the users feature
/// </summary>
[Route("/user")]
public class UserController : MyWebLogController
{
    /// <summary>
    /// Hash a password for a given user
    /// </summary>
    /// <param name="plainText">The plain-text password</param>
    /// <param name="email">The user's e-mail address</param>
    /// <param name="salt">The user-specific salt</param>
    /// <returns></returns>
    internal static string HashedPassword(string plainText, string email, Guid salt)
    {
        var allSalt = salt.ToByteArray().Concat(Encoding.UTF8.GetBytes(email)).ToArray();
        using Rfc2898DeriveBytes alg = new(plainText, allSalt, 2_048);
        return Convert.ToBase64String(alg.GetBytes(64));
    }

    /// <inheritdoc />
    public UserController(WebLogDbContext db) : base(db) { }

    [HttpGet("log-on")]
    public IActionResult LogOn() =>
        View(new LogOnModel(WebLog));

    [HttpPost("log-on")]
    public async Task<IActionResult> DoLogOn(LogOnModel model)
    {
        var user = await Db.Users.FindByEmail(model.EmailAddress);
        
        if (user == null || user.PasswordHash != HashedPassword(model.Password, user.UserName, user.Salt))
        {
            // TODO: make error, not 404
            return NotFound();
        }

        List<Claim> claims = new()
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.GivenName, user.PreferredName),
            new(ClaimTypes.Role, user.AuthorizationLevel.ToString())
        };
        ClaimsIdentity identity = new(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(identity.AuthenticationType, new(identity),
            new() { IssuedUtc = DateTime.UtcNow });

        // TODO: confirmation message

        return RedirectToAction("Index", "Admin");
    }

    [HttpGet("log-off")]
    [Authorize]
    public async Task<IActionResult> LogOff()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // TODO: confirmation message

        return LocalRedirect("~/");
    }
}
