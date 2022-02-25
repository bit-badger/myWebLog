namespace MyWebLog.Data;

/// <summary>
/// A comment on a post
/// </summary>
public class Comment
{
    /// <summary>
    /// The ID of the comment
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The ID of the post to which this comment applies
    /// </summary>
    public string PostId { get; set; } = "";

    /// <summary>
    /// The post to which this comment applies
    /// </summary>
    public Post Post { get; set; } = default!;

    /// <summary>
    /// The ID of the comment to which this comment is a reply
    /// </summary>
    public string? InReplyToId { get; set; } = null;

    /// <summary>
    /// The comment to which this comment is a reply
    /// </summary>
    public Comment? InReplyTo { get; set; } = default;

    /// <summary>
    /// The name of the commentor
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The e-mail address of the commentor
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// The URL of the commentor's personal website
    /// </summary>
    public string? Url { get; set; } = null;

    /// <summary>
    /// The status of the comment
    /// </summary>
    public CommentStatus Status { get; set; } = CommentStatus.Pending;

    /// <summary>
    /// When the comment was posted
    /// </summary>
    public DateTime PostedOn { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The text of the comment
    /// </summary>
    public string Text { get; set; } = "";
}
