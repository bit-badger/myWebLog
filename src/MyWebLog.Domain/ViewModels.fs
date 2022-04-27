namespace MyWebLog.ViewModels

open System
open MyWebLog

/// Details about a category, used to display category lists
[<NoComparison; NoEquality>]
type DisplayCategory =
    {   /// The ID of the category
        id : string
        
        /// The slug for the category
        slug : string
        
        /// The name of the category
        name : string
        
        /// A description of the category
        description : string option
        
        /// The parent category names for this (sub)category
        parentNames : string[]
    }


/// Details about a page used to display page lists
[<NoComparison; NoEquality>]
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
        
        /// The text of the page
        text : string
        
        /// The metadata for the page
        metadata : MetaItem list
    }
    /// Create a minimal display page (no text or metadata) from a database page
    static member fromPageMinimal webLog (page : Page) =
        let pageId = PageId.toString page.id
        { id             = pageId
          title          = page.title
          permalink      = Permalink.toString page.permalink
          publishedOn    = page.publishedOn
          updatedOn      = page.updatedOn
          showInPageList = page.showInPageList
          isDefault      = pageId = webLog.defaultPage
          text           = ""
          metadata       = []
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
          text           = page.text
          metadata       = page.metadata
        }


/// The model used to display the admin dashboard
[<NoComparison; NoEquality>]
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


/// View model for editing categories
[<CLIMutable; NoComparison; NoEquality>]
type EditCategoryModel =
    {   /// The ID of the category being edited
        categoryId : string
        
        /// The name of the category
        name : string
        
        /// The category's URL slug
        slug : string
        
        /// A description of the category (optional)
        description : string
        
        /// The ID of the category for which this is a subcategory (optional)
        parentId : string
    }
    
    /// Create an edit model from an existing category 
    static member fromCategory (cat : Category) =
        { categoryId  = CategoryId.toString cat.id
          name        = cat.name
          slug        = cat.slug
          description = defaultArg cat.description ""
          parentId    = cat.parentId |> Option.map CategoryId.toString |> Option.defaultValue ""
        }


/// View model to edit a page
[<CLIMutable; NoComparison; NoEquality>]
type EditPageModel =
    {   /// The ID of the page being edited
        pageId : string

        /// The title of the page
        title : string

        /// The permalink for the page
        permalink : string

        /// The template to use to display the page
        template : string
        
        /// Whether this page is shown in the page list
        isShownInPageList : bool

        /// The source format for the text
        source : string

        /// The text of the page
        text : string
        
        /// Names of metadata items
        metaNames : string[]
        
        /// Values of metadata items
        metaValues : string[]
    }
    
    /// Create an edit model from an existing page
    static member fromPage (page : Page) =
        let latest =
            match page.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let page = if page.metadata |> List.isEmpty then { page with metadata = [ MetaItem.empty ] } else page
        { pageId            = PageId.toString page.id
          title             = page.title
          permalink         = Permalink.toString page.permalink
          template          = defaultArg page.template ""
          isShownInPageList = page.showInPageList
          source            = MarkupText.sourceType latest.text
          text              = MarkupText.text       latest.text
          metaNames         = page.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          metaValues        = page.metadata |> List.map (fun m -> m.value) |> Array.ofList
        }


/// View model to edit a post
[<CLIMutable; NoComparison; NoEquality>]
type EditPostModel =
    {   /// The ID of the post being edited
        postId : string

        /// The title of the post
        title : string

        /// The permalink for the post
        permalink : string

        /// The source format for the text
        source : string

        /// The text of the post
        text : string
        
        /// The tags for the post
        tags : string
        
        /// The category IDs for the post
        categoryIds : string[]
        
        /// The post status
        status : string
        
        /// Whether this post should be published
        doPublish : bool
        
        /// Names of metadata items
        metaNames : string[]
        
        /// Values of metadata items
        metaValues : string[]
        
        /// Whether to override the published date/time
        setPublished : bool
        
        /// The published date/time to override
        pubOverride : Nullable<DateTime>
        
        /// Whether all revisions should be purged and the override date set as the updated date as well
        setUpdated : bool
    }
    /// Create an edit model from an existing past
    static member fromPost (post : Post) =
        let latest =
            match post.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let post = if post.metadata |> List.isEmpty then { post with metadata = [ MetaItem.empty ] } else post
        { postId       = PostId.toString post.id
          title        = post.title
          permalink    = Permalink.toString post.permalink
          source       = MarkupText.sourceType latest.text
          text         = MarkupText.text       latest.text
          tags         = String.Join (", ", post.tags)
          categoryIds  = post.categoryIds |> List.map CategoryId.toString |> Array.ofList
          status       = PostStatus.toString post.status
          doPublish    = false
          metaNames    = post.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          metaValues   = post.metadata |> List.map (fun m -> m.value) |> Array.ofList
          setPublished = false
          pubOverride  = Nullable<DateTime> ()
          setUpdated   = false
        }


/// The model to use to allow a user to log on
[<CLIMutable; NoComparison; NoEquality>]
type LogOnModel =
    {   /// The user's e-mail address
        emailAddress : string
    
        /// The user's password
        password : string
        
        /// Where the user should be redirected once they have logged on
        returnTo : string option
    }
    
    /// An empty log on model
    static member empty =
        { emailAddress = ""; password = ""; returnTo = None }


/// View model for posts in a list
[<NoComparison; NoEquality>]
type PostListItem =
    {   /// The ID of the post
        id : string
        
        /// The ID of the user who authored the post
        authorId : string
        
        /// The status of the post
        status : string
        
        /// The title of the post
        title : string
        
        /// The permalink for the post
        permalink : string
        
        /// When this post was published
        publishedOn : Nullable<DateTime>
        
        /// When this post was last updated
        updatedOn : DateTime
        
        /// The text of the post
        text : string
        
        /// The IDs of the categories for this post
        categoryIds : string list
        
        /// Tags for the post
        tags : string list
        
        /// Metadata for the post
        meta : MetaItem list
    }

    /// Create a post list item from a post
    static member fromPost (webLog : WebLog) (post : Post) =
        let tz = TimeZoneInfo.FindSystemTimeZoneById webLog.timeZone
        let inTZ (it : DateTime) = TimeZoneInfo.ConvertTimeFromUtc (DateTime (it.Ticks, DateTimeKind.Utc), tz)
        { id          = PostId.toString post.id
          authorId    = WebLogUserId.toString post.authorId
          status      = PostStatus.toString   post.status
          title       = post.title
          permalink   = Permalink.toString post.permalink
          publishedOn = post.publishedOn |> Option.map inTZ |> Option.toNullable
          updatedOn   = inTZ post.updatedOn
          text        = post.text
          categoryIds = post.categoryIds |> List.map CategoryId.toString
          tags        = post.tags
          meta        = post.metadata
        }


/// View model for displaying posts
type PostDisplay =
    {   /// The posts to be displayed
        posts : PostListItem[]
        
        /// Author ID -> name lookup
        authors : MetaItem list
        
        /// Category ID -> name lookup
        categories : MetaItem list
        
        /// A subtitle for the page
        subtitle : string option
        
        /// Whether there are newer posts than the ones in this model
        hasNewer : bool
        
        /// Whether there are older posts than the ones in this model
        hasOlder : bool
    }


/// View model for editing web log settings
[<CLIMutable; NoComparison; NoEquality>]
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
        
        /// The theme to use to display the web log
        themePath : string
    }
    /// Create a settings model from a web log
    static member fromWebLog (webLog : WebLog) =
        { name         = webLog.name
          subtitle     = defaultArg webLog.subtitle ""
          defaultPage  = webLog.defaultPage
          postsPerPage = webLog.postsPerPage
          timeZone     = webLog.timeZone
          themePath    = webLog.themePath
        }


[<CLIMutable; NoComparison; NoEquality>]
type UserMessage =
    {   /// The level of the message
        level : string
        
        /// The message
        message : string
        
        /// Further details about the message
        detail : string option
    }

/// Functions to support user messages
module UserMessage =
    
    /// An empty user message (use one of the others for pre-filled level)
    let empty = { level = ""; message = ""; detail = None }
    
    /// A blank success message
    let success = { empty with level = "success" }
    
    /// A blank informational message
    let info = { empty with level = "primary" }
    
    /// A blank warning message
    let warning = { empty with level = "warning" }
    
    /// A blank error message
    let error = { empty with level = "danger" }
