using Microsoft.EntityFrameworkCore;

namespace MyWebLog.Data;

/// <summary>
/// Data context for web log data
/// </summary>
public sealed class WebLogDbContext : DbContext
{
    /// <summary>
    /// Create a new ID (short GUID)
    /// </summary>
    /// <returns>A new short GUID</returns>
    /// <remarks>https://www.madskristensen.net/blog/A-shorter-and-URL-friendly-GUID</remarks>
    public static string NewId() =>
        Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('/', '_').Replace('+', '-')[..22];

    /// <summary>
    /// The categories for the web log
    /// </summary>
    public DbSet<Category> Categories { get; set; } = default!;

    /// <summary>
    /// Comments on posts
    /// </summary>
    public DbSet<Comment> Comments { get; set; } = default!;

    /// <summary>
    /// Pages
    /// </summary>
    public DbSet<Page> Pages { get; set; } = default!;

    /// <summary>
    /// Web log posts
    /// </summary>
    public DbSet<Post> Posts { get; set; } = default!;

    /// <summary>
    /// Post tags
    /// </summary>
    public DbSet<Tag> Tags { get; set; } = default!;

    /// <summary>
    /// The users of the web log
    /// </summary>
    public DbSet<WebLogUser> Users { get; set; } = default!;

    /// <summary>
    /// The details for the web log
    /// </summary>
    public DbSet<WebLogDetails> WebLogDetails { get; set; } = default!;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Configuration options</param>
    public WebLogDbContext(DbContextOptions<WebLogDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Make tables use singular names
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            entityType.SetTableName(entityType.DisplayName().Split(' ')[0]);

        // Tag and WebLogDetails use Name as its ID
        modelBuilder.Entity<Tag>().HasKey(t => t.Name);
        modelBuilder.Entity<WebLogDetails>().HasKey(wld => wld.Name);

        // Index slugs and links
        modelBuilder.Entity<Category>().HasIndex(c => c.Slug);
        modelBuilder.Entity<Page>().HasIndex(p => p.Permalink);
        modelBuilder.Entity<Post>().HasIndex(p => p.Permalink);

        // Link "author" to "user"
        modelBuilder.Entity<Page>().HasOne(p => p.Author).WithMany(wbu => wbu.Pages).HasForeignKey(p => p.AuthorId);
        modelBuilder.Entity<Post>().HasOne(p => p.Author).WithMany(wbu => wbu.Posts).HasForeignKey(p => p.AuthorId);
    }
}
