namespace MyWebLog.ViewModels

open MyWebLog
open System

/// Details about a page used to display page lists
type DisplayPage =
    {   /// The ID of this page
        id : string

        /// The title of the page
        title : string

        /// The link at which this page is displayed
        permalink : string

        /// When this page was published
        publishedOn : DateTime

        /// When this page was last updated
        updatedOn : DateTime

        /// Whether this page shows as part of the web log's navigation
        showInPageList : bool
        
        /// Is this the default page?
        isDefault : bool
    }
    /// Create a display page from a database page
    static member fromPage webLog (page : Page) =
        let pageId = PageId.toString page.id
        { id             = pageId
          title          = page.title
          permalink      = Permalink.toString page.permalink
          publishedOn    = page.publishedOn
          updatedOn      = page.updatedOn
          showInPageList = page.showInPageList
          isDefault      = pageId = webLog.defaultPage
        }


/// The model to use to allow a user to log on
[<CLIMutable>]
type LogOnModel =
    {   /// The user's e-mail address
        emailAddress : string
    
        /// The user's password
        password : string
    }


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


/// View model to edit a page
[<CLIMutable>]
type EditPageModel =
    {   /// The ID of the page being edited
        pageId : string

        /// The title of the page
        title : string

        /// The permalink for the page
        permalink : string

        /// Whether this page is shown in the page list
        isShownInPageList : bool

        /// The source format for the text
        source : string

        /// The text of the page
        text : string
    }
    /// Create an edit model from an existing page
    static member fromPage (page : Page) =
        let latest =
            match page.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        { pageId = PageId.toString page.id
          title  = page.title
          permalink = Permalink.toString page.permalink
          isShownInPageList = page.showInPageList
          source = RevisionSource.toString latest.sourceType
          text = latest.text
        }


/// View model for editing web log settings
[<CLIMutable>]
type SettingsModel =
    {   /// The name of the web log
        name : string

        /// The subtitle of the web log
        subtitle : string

        /// The default page
        defaultPage : string

        /// How many posts should appear on index pages
        postsPerPage : int

        /// The time zone in which dates/times should be displayed
        timeZone : string
    }
