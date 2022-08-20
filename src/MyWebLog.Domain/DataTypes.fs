namespace MyWebLog

open System
open MyWebLog
open NodaTime

/// A category under which a post may be identified
[<CLIMutable; NoComparison; NoEquality>]
type Category =
    {   /// The ID of the category
        Id : CategoryId

        /// The ID of the web log to which the category belongs
        WebLogId : WebLogId

        /// The displayed name
        Name : string

        /// The slug (used in category URLs)
        Slug : string

        /// A longer description of the category
        Description : string option

        /// The parent ID of this category (if a subcategory)
        ParentId : CategoryId option
    }

/// Functions to support categories
module Category =
    
    /// An empty category
    let empty =
        {   Id          = CategoryId.empty
            WebLogId    = WebLogId.empty
            Name        = ""
            Slug        = ""
            Description = None
            ParentId    = None
        }


/// A comment on a post
[<CLIMutable; NoComparison; NoEquality>]
type Comment =
    {   /// The ID of the comment
        Id : CommentId

        /// The ID of the post to which this comment applies
        PostId : PostId

        /// The ID of the comment to which this comment is a reply
        InReplyToId : CommentId option

        /// The name of the commentor
        Name : string

        /// The e-mail address of the commentor
        Email : string

        /// The URL of the commentor's personal website
        Url : string option

        /// The status of the comment
        Status : CommentStatus

        /// When the comment was posted
        PostedOn : Instant

        /// The text of the comment
        Text : string
    }

/// Functions to support comments
module Comment =
    
    /// An empty comment
    let empty =
        {   Id          = CommentId.empty
            PostId      = PostId.empty
            InReplyToId = None
            Name        = ""
            Email       = ""
            Url         = None
            Status      = Pending
            PostedOn    = Instant.MinValue
            Text        = ""
        }


/// A page (text not associated with a date/time)
[<CLIMutable; NoComparison; NoEquality>]
type Page =
    {   /// The ID of this page
        Id : PageId

        /// The ID of the web log to which this page belongs
        WebLogId : WebLogId

        /// The ID of the author of this page
        AuthorId : WebLogUserId

        /// The title of the page
        Title : string

        /// The link at which this page is displayed
        Permalink : Permalink

        /// When this page was published
        PublishedOn : Instant

        /// When this page was last updated
        UpdatedOn : Instant

        /// Whether this page shows as part of the web log's navigation
        IsInPageList : bool

        /// The template to use when rendering this page
        Template : string option

        /// The current text of the page
        Text : string

        /// Metadata for this page
        Metadata : MetaItem list
        
        /// Permalinks at which this page may have been previously served (useful for migrated content)
        PriorPermalinks : Permalink list

        /// Revisions of this page
        Revisions : Revision list
    }

/// Functions to support pages
module Page =
    
    /// An empty page
    let empty =
        {   Id              = PageId.empty
            WebLogId        = WebLogId.empty
            AuthorId        = WebLogUserId.empty
            Title           = ""
            Permalink       = Permalink.empty
            PublishedOn     = Instant.MinValue
            UpdatedOn       = Instant.MinValue
            IsInPageList    = false
            Template        = None
            Text            = ""
            Metadata        = []
            PriorPermalinks = []
            Revisions       = []
        }


/// A web log post
[<CLIMutable; NoComparison; NoEquality>]
type Post =
    {   /// The ID of this post
        Id : PostId

        /// The ID of the web log to which this post belongs
        WebLogId : WebLogId

        /// The ID of the author of this post
        AuthorId : WebLogUserId

        /// The status
        Status : PostStatus

        /// The title
        Title : string

        /// The link at which the post resides
        Permalink : Permalink

        /// The instant on which the post was originally published
        PublishedOn : Instant option

        /// The instant on which the post was last updated
        UpdatedOn : Instant

        /// The template to use in displaying the post
        Template : string option
        
        /// The text of the post in HTML (ready to display) format
        Text : string

        /// The Ids of the categories to which this is assigned
        CategoryIds : CategoryId list

        /// The tags for the post
        Tags : string list

        /// Podcast episode information for this post
        Episode : Episode option
        
        /// Metadata for the post
        Metadata : MetaItem list
        
        /// Permalinks at which this post may have been previously served (useful for migrated content)
        PriorPermalinks : Permalink list

        /// The revisions for this post
        Revisions : Revision list
    }

/// Functions to support posts
module Post =
    
    /// An empty post
    let empty =
        {   Id              = PostId.empty
            WebLogId        = WebLogId.empty
            AuthorId        = WebLogUserId.empty
            Status          = Draft
            Title           = ""
            Permalink       = Permalink.empty
            PublishedOn     = None
            UpdatedOn       = Instant.MinValue
            Text            = ""
            Template        = None
            CategoryIds     = []
            Tags            = []
            Episode         = None
            Metadata        = []
            PriorPermalinks = []
            Revisions       = []
        }


/// A mapping between a tag and its URL value, used to translate restricted characters (ex. "#1" -> "number-1")
type TagMap =
    {   /// The ID of this tag mapping
        Id : TagMapId
        
        /// The ID of the web log to which this tag mapping belongs
        WebLogId : WebLogId
        
        /// The tag which should be mapped to a different value in links
        Tag : string
        
        /// The value by which the tag should be linked
        UrlValue : string
    }

/// Functions to support tag mappings
module TagMap =
    
    /// An empty tag mapping
    let empty =
        {   Id       = TagMapId.empty
            WebLogId = WebLogId.empty
            Tag      = ""
            UrlValue = ""
        }


/// A theme
type Theme =
    {   /// The ID / path of the theme
        Id : ThemeId
        
        /// A long name of the theme
        Name : string
        
        /// The version of the theme
        Version : string
        
        /// The templates for this theme
        Templates: ThemeTemplate list
    }

/// Functions to support themes
module Theme =
    
    /// An empty theme
    let empty =
        {   Id        = ThemeId ""
            Name      = ""
            Version   = ""
            Templates = []
        }


/// A theme asset (a file served as part of a theme, at /themes/[theme]/[asset-path])
type ThemeAsset =
    {
        /// The ID of the asset (consists of theme and path)
        Id : ThemeAssetId
        
        /// The updated date (set from the file date from the ZIP archive)
        UpdatedOn : Instant
        
        /// The data for the asset
        Data : byte[]
    }

/// Functions to support theme assets
module ThemeAsset =
    
    /// An empty theme asset
    let empty =
        {   Id        = ThemeAssetId (ThemeId "", "")
            UpdatedOn = Instant.MinValue
            Data      = [||]
        }


/// An uploaded file
type Upload =
    {   /// The ID of the upload
        Id : UploadId
        
        /// The ID of the web log to which this upload belongs
        WebLogId : WebLogId
        
        /// The link at which this upload is served
        Path : Permalink
        
        /// The updated date/time for this upload
        UpdatedOn : Instant
        
        /// The data for the upload
        Data : byte[]
    }

/// Functions to support uploaded files
module Upload =
    
    /// An empty upload
    let empty =
        {   Id        = UploadId.empty
            WebLogId  = WebLogId.empty
            Path      = Permalink.empty
            UpdatedOn = Instant.MinValue
            Data      = [||]
        }


/// A web log
[<CLIMutable; NoComparison; NoEquality>]
type WebLog =
    {   /// The ID of the web log
        Id : WebLogId

        /// The name of the web log
        Name : string

        /// The slug of the web log
        Slug : string
        
        /// A subtitle for the web log
        Subtitle : string option

        /// The default page ("posts" or a page Id)
        DefaultPage : string

        /// The number of posts to display on pages of posts
        PostsPerPage : int

        /// The ID of the theme (also the path within /themes)
        ThemeId : ThemeId

        /// The URL base
        UrlBase : string

        /// The time zone in which dates/times should be displayed
        TimeZone : string
        
        /// The RSS options for this web log
        Rss : RssOptions
        
        /// Whether to automatically load htmx
        AutoHtmx : bool
        
        /// Where uploads are placed
        Uploads : UploadDestination
    }

/// Functions to support web logs
module WebLog =
    
    /// An empty web log
    let empty =
        {   Id           = WebLogId.empty
            Name         = ""
            Slug         = ""
            Subtitle     = None
            DefaultPage  = ""
            PostsPerPage = 10
            ThemeId      = ThemeId "default"
            UrlBase      = ""
            TimeZone     = ""
            Rss          = RssOptions.empty
            AutoHtmx     = false
            Uploads      = Database
        }
    
    /// Get the host (including scheme) and extra path from the URL base
    let hostAndPath webLog =
        let scheme = webLog.UrlBase.Split "://"
        let host   = scheme[1].Split "/"
        $"{scheme[0]}://{host[0]}", if host.Length > 1 then $"""/{String.Join ("/", host |> Array.skip 1)}""" else ""
    
    /// Generate an absolute URL for the given link
    let absoluteUrl webLog permalink =
        $"{webLog.UrlBase}/{Permalink.toString permalink}"

    /// Generate a relative URL for the given link
    let relativeUrl webLog permalink =
        let _, leadPath = hostAndPath webLog
        $"{leadPath}/{Permalink.toString permalink}"
    
    /// Convert an Instant (UTC reference) to the web log's local date/time
    let localTime webLog (date : Instant) =
        match DateTimeZoneProviders.Tzdb[webLog.TimeZone] with
        | null -> date.ToDateTimeUtc ()
        | tz -> date.InZone(tz).ToDateTimeUnspecified ()


/// A user of the web log
[<CLIMutable; NoComparison; NoEquality>]
type WebLogUser =
    {   /// The ID of the user
        Id : WebLogUserId

        /// The ID of the web log to which this user belongs
        WebLogId : WebLogId

        /// The user name (e-mail address)
        Email : string

        /// The user's first name
        FirstName : string

        /// The user's last name
        LastName : string

        /// The user's preferred name
        PreferredName : string

        /// The hash of the user's password
        PasswordHash : string

        /// Salt used to calculate the user's password hash
        Salt : Guid

        /// The URL of the user's personal site
        Url : string option

        /// The user's access level
        AccessLevel : AccessLevel
        
        /// When the user was created
        CreatedOn : Instant
        
        /// When the user last logged on
        LastSeenOn : Instant option
    }

/// Functions to support web log users
module WebLogUser =
    
    /// An empty web log user
    let empty =
        {   Id            = WebLogUserId.empty
            WebLogId      = WebLogId.empty
            Email         = ""
            FirstName     = ""
            LastName      = ""
            PreferredName = ""
            PasswordHash  = ""
            Salt          = Guid.Empty
            Url           = None
            AccessLevel   = Author
            CreatedOn     = Instant.FromUnixTimeSeconds 0L
            LastSeenOn    = None
        }
    
    /// Get the user's displayed name
    let displayName user =
        let name =
            seq { match user.PreferredName with "" -> user.FirstName | n -> n; " "; user.LastName }
            |> Seq.reduce (+)
        name.Trim ()
    
    /// Does a user have the required access level?
    let hasAccess level user =
        AccessLevel.hasAccess level user.AccessLevel
