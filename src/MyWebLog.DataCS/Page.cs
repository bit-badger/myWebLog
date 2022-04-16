namespace MyWebLog.Data;

/// <summary>
/// A page (text not associated with a date/time)
/// </summary>
public class Page
{
    /// <summary>
    /// The ID of this page
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The ID of the author of this page
    /// </summary>
    public string AuthorId { get; set; } = "";

    /// <summary>
    /// The author of this page
    /// </summary>
    public WebLogUser Author { get; set; } = default!;

    /// <summary>
    /// The title of the page
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The link at which this page is displayed
    /// </summary>
    public string Permalink { get; set; } = "";

    /// <summary>
    /// The instant this page was published
    /// </summary>
    public DateTime PublishedOn { get; set; } = DateTime.MinValue;

    /// <summary>
    /// The instant this page was last updated
    /// </summary>
    public DateTime UpdatedOn { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Whether this page shows as part of the web log's navigation
    /// </summary>
    public bool ShowInPageList { get; set; } = false;

    /// <summary>
    /// The template to use when rendering this page
    /// </summary>
    public string? Template { get; set; } = null;

    /// <summary>
    /// The current text of the page
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Permalinks at which this page may have been previously served (useful for migrated content)
    /// </summary>
    public ICollection<PagePermalink> PriorPermalinks { get; set; } = default!;

    /// <summary>
    /// Revisions of this page
    /// </summary>
    public ICollection<PageRevision> Revisions { get; set; } = default!;
}
