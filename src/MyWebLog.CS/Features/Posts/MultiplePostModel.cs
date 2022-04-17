namespace MyWebLog.Features.Posts;

/// <summary>
/// The model used to render multiple posts
/// </summary>
public class MultiplePostModel : MyWebLogModel
{
    /// <summary>
    /// The posts to be rendered
    /// </summary>
    public IEnumerable<Post> Posts { get; init; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="posts">The posts to be rendered</param>
    /// <param name="webLog">The details for the web log</param>
    public MultiplePostModel(IEnumerable<Post> posts, WebLogDetails webLog) : base(webLog)
    {
        Posts = posts;
    }
}
