namespace MyWebLog.Data;

/// <summary>
/// A tag
/// </summary>
public class Tag
{
    /// <summary>
    /// The name of the tag
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The posts with this tag assigned
    /// </summary>
    public ICollection<Post> Posts { get; set; } = default!;
}
