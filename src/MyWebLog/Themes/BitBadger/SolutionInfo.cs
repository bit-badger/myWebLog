using System.Reflection;
using System.Text.Json;

namespace MyWebLog.Themes.BitBadger;

/// <summary>
/// A technology used in a solution
/// </summary>
public class Technology
{
    /// <summary>
    /// The name of the technology
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Why this technology was used in this project
    /// </summary>
    public string Purpose { get; set; } = "";

    /// <summary>
    /// Whether this project currently uses this technology
    /// </summary>
    public bool? IsCurrent { get; set; } = null;
}

/// <summary>
/// Information about the solutions displayed on the front page
/// </summary>
public class FrontPageInfo
{
    /// <summary>
    /// Whether the solution should be on the front page sidebar
    /// </summary>
    public bool Display { get; set; } = false;

    /// <summary>
    /// The order in which this solution should be displayed
    /// </summary>
    public byte? Order { get; set; } = null;

    /// <summary>
    /// The description text for the front page sidebar
    /// </summary>
    public string? Text { get; set; } = null;
}

/// <summary>
/// Information about a solution
/// </summary>
public class SolutionInfo
{
    /// <summary>
    /// The name of the solution
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The URL slug for the page for this solution
    /// </summary>
    public string Slug { get; set; } = "";

    /// <summary>
    /// The URL for the solution (not the page describing it)
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// The category into which this solution falls
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// A short summary of the solution
    /// </summary>
    public string? Summary { get; set; } = null;

    /// <summary>
    /// Whether this solution is inactive
    /// </summary>
    public bool? IsInactive { get; set; } = null;

    /// <summary>
    /// Whether this solution is active
    /// </summary>
    public bool IsActive => !(IsInactive ?? false);

    /// <summary>
    /// Whether a link should not be generated to the URL for this solution
    /// </summary>
    public bool? DoNotLink { get; set; } = null;
    
    /// <summary>
    /// Whether a link should be generated to this solution
    /// </summary>
    public bool LinkToSite => !(DoNotLink ?? false);

    /// <summary>
    /// Whether an "About" link should be generated for this solution
    /// </summary>
    public bool? SkipAboutLink { get; set; } = null;

    /// <summary>
    /// Whether an "About" link should be generated for this solution
    /// </summary>
    public bool LinkToAboutPage => !(SkipAboutLink ?? false);

    /// <summary>
    /// Whether to generate a link to an archive site
    /// </summary>
    public bool? LinkToArchive { get; set; } = null;

    /// <summary>
    /// The URL of the archive site for this solution
    /// </summary>
    public string? ArchiveUrl { get; set; } = null;

    /// <summary>
    /// Home page sidebar display information
    /// </summary>
    public FrontPageInfo FrontPage { get; set; } = default!;

    /// <summary>
    /// Technologies used for this solution
    /// </summary>
    public ICollection<Technology> Technologies { get; set; } = new List<Technology>();

    /// <summary>
    /// Cache for reading solution info
    /// </summary>
    private static readonly Lazy<ValueTask<List<SolutionInfo>?>> _slnInfo = new(() =>
    {
        var asm = Assembly.GetAssembly(typeof(SolutionInfo))
            ?? throw new ArgumentNullException("Could not load the containing assembly");
        using var stream = asm.GetManifestResourceStream("MyWebLog.Themes.BitBadger.solutions.json")
            ?? throw new ArgumentNullException("Could not load the solution data");
        return JsonSerializer.DeserializeAsync<List<SolutionInfo>>(stream);
    });

    /// <summary>
    /// Get all known solutions
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException">if any required object is null</exception>
    public static async Task<ICollection<SolutionInfo>> GetAll() =>
        await _slnInfo.Value ?? throw new ArgumentNullException("Could not deserialize solution data");
}
