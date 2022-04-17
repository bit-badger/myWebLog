namespace MyWebLog.Features.Pages

open MyWebLog
open MyWebLog.Features.Shared

/// The model used to render a single page
type SinglePageModel (page : Page, webLog) =
    inherit MyWebLogModel (webLog)

    /// The page to be rendered
    member _.Page with get () = page

    /// Is this the home page?
    member _.IsHome with get() = PageId.toString page.id = webLog.defaultPage

