﻿namespace MyWebLog

open System

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

        /// The text of the post in HTML (ready to display) format
        text : string

        /// The Ids of the categories to which this is assigned
        categoryIds : CategoryId list

        /// The tags for the post
        tags : string list

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
          categoryIds     = []
          tags            = []
          priorPermalinks = []
          revisions       = []
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

        /// The path of the theme (within /views/themes)
        themePath : string

        /// The URL base
        urlBase : string

        /// The time zone in which dates/times should be displayed
        timeZone : string
    }

/// Functions to support web logs
module WebLog =
    
    /// An empty set of web logs
    let empty =
        { id           = WebLogId.empty
          name         = ""
          subtitle     = None
          defaultPage  = ""
          postsPerPage = 10
          themePath    = "default"
          urlBase      = ""
          timeZone     = ""
        }
    
    /// Convert a permalink to an absolute URL
    let absoluteUrl webLog = function Permalink link -> $"{webLog.urlBase}{link}"


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