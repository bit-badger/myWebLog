namespace MyWebLog.Data;

/// <summary>
/// The source format for a revision
/// </summary>
public enum RevisionSource
{
    /// <summary>Markdown text</summary>
    Markdown,
    /// <summary>HTML</summary>
    Html
}

/// <summary>
/// A level of authorization for a given web log
/// </summary>
public enum AuthorizationLevel
{
    /// <summary>The user may administer all aspects of a web log</summary>
    Administrator,
    /// <summary>The user is a known user of a web log</summary>
    User
}

/// <summary>
/// Statuses for posts
/// </summary>
public enum PostStatus
{
    /// <summary>The post should not be publicly available</summary>
    Draft,
    /// <summary>The post is publicly viewable</summary>
    Published
}

/// <summary>
/// Statuses for post comments
/// </summary>
public enum CommentStatus
{
    /// <summary>The comment is approved</summary>
    Approved,
    /// <summary>The comment has yet to be approved</summary>
    Pending,
    /// <summary>The comment was unsolicited and unwelcome</summary>
    Spam
}
