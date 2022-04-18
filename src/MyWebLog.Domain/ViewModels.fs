namespace MyWebLog.ViewModels

open MyWebLog

/// Base model class for myWebLog views
type MyWebLogModel (webLog : WebLog) =

    /// The details for the web log
    member val WebLog = webLog with get


/// The model to use to allow a user to log on
[<CLIMutable>]
type LogOnModel =
    {   /// The user's e-mail address
        emailAddress : string
    
        /// The user's password
        password : string
    }


/// The model used to render a single page
type SinglePageModel =
    {   /// The page to be rendered
        page : Page
        
        /// The web log to which the page belongs
        webLog : WebLog
    }
    /// Is this the home page?
    member this.isHome with get () = PageId.toString this.page.id = this.webLog.defaultPage


/// The model used to display the admin dashboard
type DashboardModel =
    {   /// The number of published posts
        posts : int

        /// The number of post drafts
        drafts : int

        /// The number of pages
        pages : int

        /// The number of pages in the page list
        listedPages : int

        /// The number of categories
        categories : int

        /// The top-level categories
        topLevelCategories : int
    }