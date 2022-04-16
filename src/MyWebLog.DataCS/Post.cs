namespace MyWebLog.Data;

/// <summary>
/// A web log post
/// </summary>
public class Post
{
    /// <summary>
    /// The ID of this post
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The ID of the author of this post
    /// </summary>
    public string AuthorId { get; set; } = "";

    /// <summary>
    /// The author of the post
    /// </summary>
    public WebLogUser Author { get; set; } = default!;

    /// <summary>
    /// The status
    /// </summary>
    public PostStatus Status { get; set; } = PostStatus.Draft;

    /// <summary>
    /// The title
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The link at which the post resides
    /// </summary>
    public string Permalink { get; set; } = "";

    /// <summary>
    /// The instant on which the post was originally published
    /// </summary>
    public DateTime? PublishedOn { get; set; } = null;

    /// <summary>
    /// The instant on which the post was last updated
    /// </summary>
    public DateTime UpdatedOn { get; set; } = DateTime.MinValue;

    /// <summary>
    /// The text of the post in HTML (ready to display) format
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// The Ids of the categories to which this is assigned
    /// </summary>
    public ICollection<Category> Categories { get; set; } = default!;

    /// <summary>
    /// The tags for the post
    /// </summary>
    public ICollection<Tag> Tags { get; set; } = default!;

    /// <summary>
    /// Permalinks at which this post may have been previously served (useful for migrated content)
    /// </summary>
    public ICollection<PostPermalink> PriorPermalinks { get; set; } = default!;

    /// <summary>
    /// The revisions for this post
    /// </summary>
    public ICollection<PostRevision> Revisions { get; set; } = default!;
}
