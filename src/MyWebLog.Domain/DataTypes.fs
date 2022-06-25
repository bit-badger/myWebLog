namespace MyWebLog

open System
open MyWebLog

/// A category under which a post may be identified
[<CLIMutable; NoComparison; NoEquality>]
type Category =
    {   /// The ID of the category
        id : CategoryId

        /// The ID of the web log to which the category belongs
        webLogId : WebLogId

        /// The displayed name
        name : string

        /// The slug (used in category URLs)
        slug : string

        /// A longer description of the category
        description : string option

        /// The parent ID of this category (if a subcategory)
        parentId : CategoryId option
    }

/// Functions to support categories
module Category =
    
    /// An empty category
    let empty =
        { id          = CategoryId.empty
          webLogId    = WebLogId.empty
          name        = ""
          slug        = ""
          description = None
          parentId    = None
        }


/// A comment on a post
[<CLIMutable; NoComparison; NoEquality>]
type Comment =
    {   /// The ID of the comment
        id : CommentId

        /// The ID of the post to which this comment applies
        postId : PostId

        /// The ID of the comment to which this comment is a reply
        inReplyToId : CommentId option

        /// The name of the commentor
        name : string

        /// The e-mail address of the commentor
        email : string

        /// The URL of the commentor's personal website
        url : string option

        /// The status of the comment
        status : CommentStatus

        /// When the comment was posted
        postedOn : DateTime

        /// The text of the comment
        text : string
    }

/// Functions to support comments
module Comment =
    
    /// An empty comment
    let empty =
        { id          = CommentId.empty
          postId      = PostId.empty
          inReplyToId = None
          name        = ""
          email       = ""
          url         = None
          status      = Pending
          postedOn    = DateTime.UtcNow
          text        = ""
        }


/// A page (text not associated with a date/time)
[<CLIMutable; NoComparison; NoEquality>]
type Page =
    {   /// The ID of this page
        id : PageId

        /// The ID of the web log to which this page belongs
        webLogId : WebLogId

        /// The ID of the author of this page
        authorId : WebLogUserId

        /// The title of the page
        title : string

        /// The link at which this page is displayed
        permalink : Permalink

        /// When this page was published
        publishedOn : DateTime

        /// When this page was last updated
        updatedOn : DateTime

        /// Whether this page shows as part of the web log's navigation
        showInPageList : bool

        /// The template to use when rendering this page
        template : string option

        /// The current text of the page
        text : string

        /// Metadata for this page
        metadata : MetaItem list
        
        /// Permalinks at which this page may have been previously served (useful for migrated content)
        priorPermalinks : Permalink list

        /// Revisions of this page
        revisions : Revision list
    }

/// Functions to support pages
module Page =
    
    /// An empty page
    let empty =
        { id              = PageId.empty
          webLogId        = WebLogId.empty
          authorId        = WebLogUserId.empty
          title           = ""
          permalink       = Permalink.empty
          publishedOn     = DateTime.MinValue
          updatedOn       = DateTime.MinValue
          showInPageList  = false
          template        = None
          text            = ""
          metadata        = []
          priorPermalinks = []
          revisions       = []
        }


/// A web log post
[<CLIMutable; NoComparison; NoEquality>]
type Post =
    {   /// The ID of this post
        id : PostId

        /// The ID of the web log to which this post belongs
        webLogId : WebLogId

        /// The ID of the author of this post
        authorId : WebLogUserId

        /// The status
        status : PostStatus

        /// The title
        title : string

        /// The link at which the post resides
        permalink : Permalink

        /// The instant on which the post was originally published
        publishedOn : DateTime option

        /// The instant on which the post was last updated
        updatedOn : DateTime

        /// The template to use in displaying the post
        template : string option
        
        /// The text of the post in HTML (ready to display) format
        text : string

        /// The Ids of the categories to which this is assigned
        categoryIds : CategoryId list

        /// The tags for the post
        tags : string list

        /// Podcast episode information for this post
        episode : Episode option
        
        /// Metadata for the post
        metadata : MetaItem list
        
        /// Permalinks at which this post may have been previously served (useful for migrated content)
        priorPermalinks : Permalink list

        /// The revisions for this post
        revisions : Revision list
    }

/// Functions to support posts
module Post =
    
    /// An empty post
    let empty =
        { id              = PostId.empty
          webLogId        = WebLogId.empty
          authorId        = WebLogUserId.empty
          status          = Draft
          title           = ""
          permalink       = Permalink.empty
          publishedOn     = None
          updatedOn       = DateTime.MinValue
          text            = ""
          template        = None
          categoryIds     = []
          tags            = []
          episode         = None
          metadata        = []
          priorPermalinks = []
          revisions       = []
        }


/// A mapping between a tag and its URL value, used to translate restricted characters (ex. "#1" -> "number-1")
type TagMap =
    {   /// The ID of this tag mapping
        id : TagMapId
        
        /// The ID of the web log to which this tag mapping belongs
        webLogId : WebLogId
        
        /// The tag which should be mapped to a different value in links
        tag : string
        
        /// The value by which the tag should be linked
        urlValue : string
    }

/// Functions to support tag mappings
module TagMap =
    
    /// An empty tag mapping
    let empty =
        { id       = TagMapId.empty
          webLogId = WebLogId.empty
          tag      = ""
          urlValue = ""
        }


/// A theme
type Theme =
    {   /// The ID / path of the theme
        id : ThemeId
        
        /// A long name of the theme
        name : string
        
        /// The version of the theme
        version : string
        
        /// The templates for this theme
        templates: ThemeTemplate list
    }

/// Functions to support themes
module Theme =
    
    /// An empty theme
    let empty =
        { id        = ThemeId ""
          name      = ""
          version   = ""
          templates = []
        }


/// A theme asset (a file served as part of a theme, at /themes/[theme]/[asset-path])
type ThemeAsset =
    {
        /// The ID of the asset (consists of theme and path)
        id : ThemeAssetId
        
        /// The updated date (set from the file date from the ZIP archive)
        updatedOn : DateTime
        
        /// The data for the asset
        data : byte[]
    }


/// A web log
[<CLIMutable; NoComparison; NoEquality>]
type WebLog =
    {   /// The ID of the web log
        id : WebLogId

        /// The name of the web log
        name : string

        /// A subtitle for the web log
        subtitle : string option

        /// The default page ("posts" or a page Id)
        defaultPage : string

        /// The number of posts to display on pages of posts
        postsPerPage : int

        /// The path of the theme (within /themes)
        themePath : string

        /// The URL base
        urlBase : string

        /// The time zone in which dates/times should be displayed
        timeZone : string
        
        /// The RSS options for this web log
        rss : RssOptions
        
        /// Whether to automatically load htmx
        autoHtmx : bool
    }

/// Functions to support web logs
module WebLog =
    
    /// An empty web log
    let empty =
        { id           = WebLogId.empty
          name         = ""
          subtitle     = None
          defaultPage  = ""
          postsPerPage = 10
          themePath    = "default"
          urlBase      = ""
          timeZone     = ""
          rss          = RssOptions.empty
          autoHtmx     = false
        }
    
    /// Get the host (including scheme) and extra path from the URL base
    let hostAndPath webLog =
        let scheme = webLog.urlBase.Split "://"
        let host   = scheme[1].Split "/"
        $"{scheme[0]}://{host[0]}", if host.Length > 1 then $"""/{String.Join ("/", host |> Array.skip 1)}""" else ""
    
    /// Generate an absolute URL for the given link
    let absoluteUrl webLog permalink =
        $"{webLog.urlBase}/{Permalink.toString permalink}"

    /// Generate a relative URL for the given link
    let relativeUrl webLog permalink =
        let _, leadPath = hostAndPath webLog
        $"{leadPath}/{Permalink.toString permalink}"
    
    /// Convert a UTC date/time to the web log's local date/time
    let localTime webLog (date : DateTime) =
        TimeZoneInfo.ConvertTimeFromUtc
            (DateTime (date.Ticks, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById webLog.timeZone) 

    /// Convert a date/time in the web log's local date/time to UTC
    let utcTime webLog (date : DateTime) =
        TimeZoneInfo.ConvertTimeToUtc
            (DateTime (date.Ticks, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById webLog.timeZone)


/// A user of the web log
[<CLIMutable; NoComparison; NoEquality>]
type WebLogUser =
    {   /// The ID of the user
        id : WebLogUserId

        /// The ID of the web log to which this user belongs
        webLogId : WebLogId

        /// The user name (e-mail address)
        userName : string

        /// The user's first name
        firstName : string

        /// The user's last name
        lastName : string

        /// The user's preferred name
        preferredName : string

        /// The hash of the user's password
        passwordHash : string

        /// Salt used to calculate the user's password hash
        salt : Guid

        /// The URL of the user's personal site
        url : string option

        /// The user's authorization level
        authorizationLevel : AuthorizationLevel
    }

/// Functions to support web log users
module WebLogUser =
    
    /// An empty web log user
    let empty =
        { id                 = WebLogUserId.empty
          webLogId           = WebLogId.empty
          userName           = ""
          firstName          = ""
          lastName           = ""
          preferredName      = ""
          passwordHash       = ""
          salt               = Guid.Empty
          url                = None
          authorizationLevel = User
        }
    
    /// Get the user's displayed name
    let displayName user =
        let name =
            seq { match user.preferredName with "" -> user.firstName | n -> n; " "; user.lastName }
            |> Seq.reduce (+)
        name.Trim ()
