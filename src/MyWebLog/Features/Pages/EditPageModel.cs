using System.ComponentModel.DataAnnotations;

namespace MyWebLog.Features.Pages;

/// <summary>
/// Model used to edit pages
/// </summary>
public class EditPageModel : MyWebLogModel
{
    /// <summary>
    /// The ID of the page being edited
    /// </summary>
    public string PageId { get; set; } = "new";

    /// <summary>
    /// Whether this is a new page
    /// </summary>
    public bool IsNew => PageId == "new";

    /// <summary>
    /// The title of the page
    /// </summary>
    [Display(ResourceType = typeof(Resources), Name = "Title")]
    [Required(AllowEmptyStrings = false)]
    public string Title { get; set; } = "";

    /// <summary>
    /// The permalink for the page
    /// </summary>
    [Display(ResourceType = typeof(Resources), Name = "Permalink")]
    [Required(AllowEmptyStrings = false)]
    public string Permalink { get; set; } = "";

    /// <summary>
    /// Whether this page is shown in the page list
    /// </summary>
    [Display(ResourceType = typeof(Resources), Name = "ShowInPageList")]
    public bool IsShownInPageList { get; set; } = false;

    /// <summary>
    /// The source format for the text
    /// </summary>
    public RevisionSource Source { get; set; } = RevisionSource.Html;

    /// <summary>
    /// The text of the page
    /// </summary>
    [Display(ResourceType = typeof(Resources), Name = "PageText")]
    [Required(AllowEmptyStrings = false)]
    public string Text { get; set; } = "";

    [Obsolete("Only used for model binding; use the WebLogDetails constructor")]
    public EditPageModel() : base(new()) { }

    /// <inheritdoc />
    public EditPageModel(WebLogDetails webLog) : base(webLog) { }

    /// <summary>
    /// Create a model from an existing page
    /// </summary>
    /// <param name="page">The page from which the model will be created</param>
    /// <param name="webLog">The web log to which the page belongs</param>
    /// <returns>A populated model</returns>
    public static EditPageModel CreateFromPage(Page page, WebLogDetails webLog)
    {
        var lastRev = page.Revisions.OrderByDescending(r => r.AsOf).First();
        return new(webLog)
        {
            PageId = page.Id,
            Title = page.Title,
            Permalink = page.Permalink,
            IsShownInPageList = page.ShowInPageList,
            Source = lastRev.SourceType,
            Text = lastRev.Text
        };
    }

    /// <summary>
    /// Populate a page from the values contained in this page
    /// </summary>
    /// <param name="page">The page to be populated</param>
    /// <returns>The populated page</returns>
    public Page? PopulatePage(Page? page)
    {
        if (page == null) return null;

        page.Title = Title;
        page.Permalink = Permalink;
        page.ShowInPageList = IsShownInPageList;
        page.Revisions.Add(new()
        {
            Id = WebLogDbContext.NewId(),
            AsOf = DateTime.UtcNow,
            SourceType = Source,
            Text = Text
        });
        return page;
    }
}
