namespace MyWebLog.Data;

/// <summary>
/// A revision of a page or post
/// </summary>
public abstract class Revision
{
    /// <summary>
    /// The ID of this revision
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// When this revision was saved
    /// </summary>
    public DateTime AsOf { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The source language (Markdown or HTML)
    /// </summary>
    public RevisionSource SourceType { get; set; } = RevisionSource.Html;

    /// <summary>
    /// The text of the revision
    /// </summary>
    public string Text { get; set; } = "";
}

/// <summary>
/// A revision of a page
/// </summary>
public class PageRevision : Revision
{
    /// <summary>
    /// The ID of the page to which this revision belongs
    /// </summary>
    public string PageId { get; set; } = "";

    /// <summary>
    /// The page to which this revision belongs
    /// </summary>
    public Page Page { get; set; } = default!;
}

/// <summary>
/// A revision of a post
/// </summary>
public class PostRevision : Revision
{
    /// <summary>
    /// The ID of the post to which this revision applies
    /// </summary>
    public string PostId { get; set; } = "";

    /// <summary>
    /// The post to which this revision applies
    /// </summary>
    public Post Post { get; set; } = default!;
}
