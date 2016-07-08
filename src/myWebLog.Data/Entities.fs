namespace myWebLog.Entities

open Newtonsoft.Json

// ---- Constants ----

/// Constants to use for revision source language
module RevisionSource =
  let Markdown = "markdown"
  let HTML     = "html"

/// Constants to use for authorization levels
module AuthorizationLevel =
  let Administrator = "Administrator"
  let User          = "User"

/// Constants to use for post statuses
module PostStatus =
  let Draft     = "Draft"
  let Published = "Published"

// ---- Entities ----

/// A revision of a post or page
type Revision = {
  /// The instant which this revision was saved
  asOf : int64
  /// The source language
  sourceType : string
  /// The text
  text : string
  }


/// A page with static content
type Page = {
  /// The Id
  id : string
  /// The Id of the web log to which this page belongs
  webLogId : string
  /// The Id of the author of this page
  authorId : string
  /// The title of the page
  title : string
  /// The link at which this page is displayed
  permalink : string
  /// The instant this page was published
  publishedOn : int64
  /// The instant this page was last updated
  lastUpdatedOn : int64
  /// Whether this page shows as part of the web log's navigation
  showInPageList : bool
  /// The current text of the page
  text : string
  /// Revisions of this page
  revisions : Revision list
  }
with
  static member empty = 
    { id             = ""
      webLogId       = ""
      authorId       = ""
      title          = ""
      permalink      = ""
      publishedOn    = int64 0
      lastUpdatedOn  = int64 0
      showInPageList = false
      text           = ""
      revisions      = List.empty
    }


/// A web log
type WebLog = {
  /// The Id
  id : string
  /// The name
  name : string
  /// The subtitle
  subtitle : string option
  /// The default page ("posts" or a page Id)
  defaultPage : string
  /// The path of the theme (within /views)
  themePath : string
  /// The URL base
  urlBase : string
  /// The time zone in which dates/times should be displayed
  timeZone : string
  /// A list of pages to be rendered as part of the site navigation
  [<JsonIgnore>]
  pageList : Page list
  }
with
  /// An empty web log
  static member empty =
    { id          = ""
      name        = ""
      subtitle    = None
      defaultPage = ""
      themePath   = "default"
      urlBase     = ""
      timeZone    = "America/New_York"
      pageList    = List.empty
      }


/// An authorization between a user and a web log
type Authorization = {
  /// The Id of the web log to which this authorization grants access
  webLogId : string
  /// The level of access granted by this authorization
  level : string
  }


/// A user of myWebLog
type User = {
  /// The Id
  id : string
  /// The user name (e-mail address)
  userName : string
  /// The first name
  firstName : string
  /// The last name
  lastName : string
  /// The user's preferred name
  preferredName : string
  /// The hash of the user's password
  passwordHash : string
  /// The URL of the user's personal site
  url : string option
  /// The user's authorizations
  authorizations : Authorization list
  }
with
  /// An empty user
  static member empty =
    { id             = ""
      userName       = ""
      firstName      = ""
      lastName       = ""
      preferredName  = ""
      passwordHash   = ""
      url            = None
      authorizations = List.empty
    }
  
  /// Claims for this user
  [<JsonIgnore>]
  member this.claims = this.authorizations
                       |> List.map (fun auth -> sprintf "%s|%s" auth.webLogId auth.level)
    

/// A category to which posts may be assigned
type Category = {
  /// The Id
  id : string
  /// The displayed name
  name : string
  /// The slug (used in category URLs)
  slug : string
  /// A longer description of the category
  description : string option
  /// The parent Id of this category (if a subcategory)
  parentId : string option
  /// The categories for which this category is the parent
  children : string list
  }
with
  /// An empty category
  static member empty =
    { id          = ""
      name        = ""
      slug        = ""
      description = None
      parentId    = None
      children    = List.empty
      }


/// A comment (applies to a post)
type Comment = {
  /// The Id
  id : string
  /// The Id of the post to which this comment applies
  postId : string
  /// The Id of the comment to which this comment is a reply
  inReplyToId : string option
  /// The name of the commentor
  name : string
  /// The e-mail address of the commentor
  email : string
  /// The URL of the commentor's personal website
  url : string option
  /// The instant the comment was posted
  postedOn : int64
  /// The text of the comment
  text : string
  }
with
  static member empty =
    { id          = ""
      postId      = ""
      inReplyToId = None
      name        = ""
      email       = ""
      url         = None
      postedOn    = int64 0
      text        = ""
    }


/// A post
type Post = {
  /// The Id
  id : string
  /// The Id of the web log to which this post belongs
  webLogId : string
  /// The Id of the author of this post
  authorId : string
  /// The status
  status : string
  /// The title
  title : string
  /// The link at which the post resides
  permalink : string
  /// The instant on which the post was originally published
  publishedOn : int64
  /// The instant on which the post was last updated
  updatedOn : int64
  /// The text of the post
  text : string
  /// The Ids of the categories to which this is assigned
  categoryIds : string list
  /// The tags for the post
  tags : string list
  /// The permalinks at which this post may have once resided
  priorPermalinks : string list
  /// Revisions of this post
  revisions : Revision list
  /// The categories to which this is assigned
  [<JsonIgnore>]
  categories : Category list
  /// The comments
  [<JsonIgnore>]
  comments : Comment list
  }
with
  static member empty =
    { id              = ""
      webLogId        = ""
      authorId        = ""
      status          = PostStatus.Draft
      title           = ""
      permalink       = ""
      publishedOn     = int64 0
      updatedOn       = int64 0
      text            = ""
      categoryIds     = List.empty
      tags            = List.empty
      priorPermalinks = List.empty
      revisions       = List.empty
      categories      = List.empty
      comments        = List.empty
    }
