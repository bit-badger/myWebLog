namespace MyWebLog.Features.Admin

open MyWebLog
open MyWebLog.Features.Shared

/// The model used to display the dashboard
type DashboardModel (webLog) =
    inherit MyWebLogModel (webLog)

    /// The number of published posts
    member val Posts = 0 with get, set

    /// The number of post drafts
    member val Drafts = 0 with get, set

    /// The number of pages
    member val Pages = 0 with get, set

    /// The number of pages in the page list
    member val ListedPages = 0 with get, set

    /// The number of categories
    member val Categories = 0 with get, set

    /// The top-level categories
    member val TopLevelCategories = 0 with get, set


open Microsoft.AspNetCore.Mvc.Rendering
open System.ComponentModel.DataAnnotations

/// View model for editing web log settings
type SettingsModel (webLog) =
    inherit MyWebLogModel (webLog)

    /// Default constructor
    [<System.Obsolete "Only used for model binding; use the WebLogDetails constructor">]
    new() = SettingsModel WebLog.empty

    /// The name of the web log
    [<Required (AllowEmptyStrings = false)>]
    [<Display ( ResourceType = typeof<Resources>, Name = "Name")>]
    member val Name = webLog.name with get, set

    /// The subtitle of the web log
    [<Display(ResourceType = typeof<Resources>, Name = "Subtitle")>]
    member val Subtitle = (defaultArg webLog.subtitle "") with get, set

    /// The default page
    [<Required>]
    [<Display(ResourceType = typeof<Resources>, Name = "DefaultPage")>]
    member val DefaultPage = webLog.defaultPage with get, set

    /// How many posts should appear on index pages
    [<Required>]
    [<Display(ResourceType = typeof<Resources>, Name = "PostsPerPage")>]
    [<Range(0, 50)>]
    member val PostsPerPage = webLog.postsPerPage with get, set

    /// The time zone in which dates/times should be displayed
    [<Required>]
    [<Display(ResourceType = typeof<Resources>, Name = "TimeZone")>]
    member val TimeZone = webLog.timeZone with get, set

    /// Possible values for the default page
    member val DefaultPages = Seq.empty<SelectListItem> with get, set

    /// Update the settings object from the data in this form
    member this.UpdateSettings (settings : WebLog) =
        { settings with
            name = this.Name
            subtitle = (match this.Subtitle with "" -> None | sub -> Some sub)
            defaultPage = this.DefaultPage
            postsPerPage = this.PostsPerPage
            timeZone = this.TimeZone
        }
