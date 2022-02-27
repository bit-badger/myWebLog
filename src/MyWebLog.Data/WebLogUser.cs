namespace MyWebLog.Data;

/// <summary>
/// A user of the web log
/// </summary>
public class WebLogUser
{
    /// <summary>
    /// The ID of the user
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The user name (e-mail address)
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// The user's first name
    /// </summary>
    public string FirstName { get; set; } = "";

    /// <summary>
    /// The user's last name
    /// </summary>
    public string LastName { get; set; } = "";

    /// <summary>
    /// The user's preferred name
    /// </summary>
    public string PreferredName { get; set; } = "";

    /// <summary>
    /// The hash of the user's password
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// Salt used to calculate the user's password hash
    /// </summary>
    public Guid Salt { get; set; } = Guid.Empty;

    /// <summary>
    /// The URL of the user's personal site
    /// </summary>
    public string? Url { get; set; } = null;

    /// <summary>
    /// The user's authorization level
    /// </summary>
    public AuthorizationLevel AuthorizationLevel { get; set; } = AuthorizationLevel.User;

    /// <summary>
    /// Pages written by this author
    /// </summary>
    public ICollection<Page> Pages { get; set; } = default!;

    /// <summary>
    /// Posts written by this author
    /// </summary>
    public ICollection<Post> Posts { get; set; } = default!;
}
