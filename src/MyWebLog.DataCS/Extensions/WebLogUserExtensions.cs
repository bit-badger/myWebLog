using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

public static class WebLogUserExtensions
{
    /// <summary>
    /// Find a user by their log on information (non-tracked)
    /// </summary>
    /// <param name="email">The user's e-mail address</param>
    /// <param name="pwHash">The hash of the password provided by the user</param>
    /// <returns>The user, if the credentials match; null if they do not</returns>
    public static async Task<WebLogUser?> FindByEmail(this DbSet<WebLogUser> db, string email) =>
        await db.SingleOrDefaultAsync(wlu => wlu.UserName == email).ConfigureAwait(false);

}
