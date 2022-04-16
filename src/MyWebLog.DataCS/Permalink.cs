namespace MyWebLog.Data;

/// <summary>
/// A permalink which a post or page used to have
/// </summary>
public abstract class Permalink
{
    /// <summary>
    /// The ID of this permalink
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The link
    /// </summary>
    public string Url { get; set; } = "";
}

/// <summary>
/// A prior permalink for a page
/// </summary>
public class PagePermalink : Permalink
{
    /// <summary>
    /// The ID of the page to which this permalink belongs
    /// </summary>
    public string PageId { get; set; } = "";

    /// <summary>
    /// The page to which this permalink belongs
    /// </summary>
    public Page Page { get; set; } = default!;
}

/// <summary>
/// A prior permalink for a post
/// </summary>
public class PostPermalink : Permalink
{
    /// <summary>
    /// The ID of the post to which this permalink belongs
    /// </summary>
    public string PostId { get; set; } = "";

    /// <summary>
    /// The post to which this permalink belongs
    /// </summary>
    public Post Post { get; set; } = default!;
}
