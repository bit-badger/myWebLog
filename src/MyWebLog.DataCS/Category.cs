namespace MyWebLog.Data;

/// <summary>
/// A category under which a post may be identfied
/// </summary>
public class Category
{
    /// <summary>
    /// The ID of the category
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The displayed name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The slug (used in category URLs)
    /// </summary>
    public string Slug { get; set; } = "";

    /// <summary>
    /// A longer description of the category
    /// </summary>
    public string? Description { get; set; } = null;

    /// <summary>
    /// The parent ID of this category (if a subcategory)
    /// </summary>
    public string? ParentId { get; set; } = null;

    /// <summary>
    /// The parent of this category (if a subcategory)
    /// </summary>
    public Category? Parent { get; set; } = default;

    /// <summary>
    /// The posts assigned to this category
    /// </summary>
    public ICollection<Post> Posts { get; set; } = default!;
}
