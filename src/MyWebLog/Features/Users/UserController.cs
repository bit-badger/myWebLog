using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace MyWebLog.Features.Users;

/// <summary>
/// Controller for the users feature
/// </summary>
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
        using var alg = new Rfc2898DeriveBytes(plainText, allSalt, 2_048);
        return Convert.ToBase64String(alg.GetBytes(64));
    }

    /// <inheritdoc />
    public UserController(WebLogDbContext db) : base(db) { }

    public IActionResult Index()
    {
        return View();
    }
}
