namespace MyWebLog.Entities

open Newtonsoft.Json

// ---- Constants ----

/// Constants to use for revision source language
[<RequireQualifiedAccess>]
module RevisionSource =
  [<Literal>]
  let Markdown = "markdown"
  [<Literal>]
  let HTML = "html"

/// Constants to use for authorization levels
[<RequireQualifiedAccess>]
module AuthorizationLevel =
  [<Literal>]
  let Administrator = "Administrator"
  [<Literal>]
  let User = "User"

/// Constants to use for post statuses
[<RequireQualifiedAccess>]
module PostStatus =
  [<Literal>]
  let Draft = "Draft"
  [<Literal>]
  let Published = "Published"

/// Constants to use for comment statuses
[<RequireQualifiedAccess>]
module CommentStatus =
  [<Literal>]
  let Approved = "Approved"
  [<Literal>]
  let Pending = "Pending"
  [<Literal>]
  let Spam = "Spam"

// ---- Entities ----

/// A revision of a post or page
type Revision =
  { /// The instant which this revision was saved
    AsOf : int64
    /// The source language
    SourceType : string
    /// The text
    Text : string }
with
  /// An empty revision
  static member Empty =
    { AsOf       = int64 0
      SourceType = RevisionSource.HTML
      Text       = "" }

/// A page with static content
type Page =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The Id of the web log to which this page belongs
    WebLogId : string
    /// The Id of the author of this page
    AuthorId : string
    /// The title of the page
    Title : string
    /// The link at which this page is displayed
    Permalink : string
    /// The instant this page was published
    PublishedOn : int64
    /// The instant this page was last updated
    UpdatedOn : int64
    /// Whether this page shows as part of the web log's navigation
    ShowInPageList : bool
    /// The current text of the page
    Text : string
    /// Revisions of this page
    Revisions : Revision list }
with
  static member Empty = 
    { Id             = ""
      WebLogId       = ""
      AuthorId       = ""
      Title          = ""
      Permalink      = ""
      PublishedOn    = int64 0
      UpdatedOn      = int64 0
      ShowInPageList = false
      Text           = ""
      Revisions      = []
    }


/// An entry in the list of pages displayed as part of the web log (derived via query)
type PageListEntry =
  { Permalink : string
    Title     : string }

/// A web log
type WebLog =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The name
    Name : string
    /// The subtitle
    Subtitle : string option
    /// The default page ("posts" or a page Id)
    DefaultPage : string
    /// The path of the theme (within /views/themes)
    ThemePath : string
    /// The URL base
    UrlBase : string
    /// The time zone in which dates/times should be displayed
    TimeZone : string
    /// A list of pages to be rendered as part of the site navigation (not stored)
    PageList : PageListEntry list }
with
  /// An empty web log
  static member Empty =
    { Id          = ""
      Name        = ""
      Subtitle    = None
      DefaultPage = ""
      ThemePath   = "default"
      UrlBase     = ""
      TimeZone    = "America/New_York"
      PageList    = [] }


/// An authorization between a user and a web log
type Authorization =
  { /// The Id of the web log to which this authorization grants access
    WebLogId : string
    /// The level of access granted by this authorization
    Level : string }


/// A user of myWebLog
type User =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The user name (e-mail address)
    UserName : string
    /// The first name
    FirstName : string
    /// The last name
    LastName : string
    /// The user's preferred name
    PreferredName : string
    /// The hash of the user's password
    PasswordHash : string
    /// The URL of the user's personal site
    Url : string option
    /// The user's authorizations
    Authorizations : Authorization list }
with
  /// An empty user
  static member Empty =
    { Id             = ""
      UserName       = ""
      FirstName      = ""
      LastName       = ""
      PreferredName  = ""
      PasswordHash   = ""
      Url            = None
      Authorizations = [] }
  
  /// Claims for this user
  [<JsonIgnore>]
  member this.Claims = this.Authorizations
                       |> List.map (fun auth -> sprintf "%s|%s" auth.WebLogId auth.Level)
    

/// A category to which posts may be assigned
type Category =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The Id of the web log to which this category belongs
    WebLogId : string
    /// The displayed name
    Name : string
    /// The slug (used in category URLs)
    Slug : string
    /// A longer description of the category
    Description : string option
    /// The parent Id of this category (if a subcategory)
    ParentId : string option
    /// The categories for which this category is the parent
    Children : string list }
with
  /// An empty category
  static member Empty =
    { Id          = "new"
      WebLogId    = ""
      Name        = ""
      Slug        = ""
      Description = None
      ParentId    = None
      Children    = [] }


/// A comment (applies to a post)
type Comment =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The Id of the post to which this comment applies
    PostId : string
    /// The Id of the comment to which this comment is a reply
    InReplyToId : string option
    /// The name of the commentor
    Name : string
    /// The e-mail address of the commentor
    Email : string
    /// The URL of the commentor's personal website
    Url : string option
    /// The status of the comment
    Status : string
    /// The instant the comment was posted
    PostedOn : int64
    /// The text of the comment
    Text : string }
with
  static member Empty =
    { Id          = ""
      PostId      = ""
      InReplyToId = None
      Name        = ""
      Email       = ""
      Url         = None
      Status      = CommentStatus.Pending
      PostedOn    = int64 0
      Text        = "" }


/// A post
type Post =
  { /// The Id
    [<JsonProperty("id")>]
    Id : string
    /// The Id of the web log to which this post belongs
    WebLogId : string
    /// The Id of the author of this post
    AuthorId : string
    /// The status
    Status : string
    /// The title
    Title : string
    /// The link at which the post resides
    Permalink : string
    /// The instant on which the post was originally published
    PublishedOn : int64
    /// The instant on which the post was last updated
    UpdatedOn : int64
    /// The text of the post
    Text : string
    /// The Ids of the categories to which this is assigned
    CategoryIds : string list
    /// The tags for the post
    Tags : string list
    /// The permalinks at which this post may have once resided
    PriorPermalinks : string list
    /// Revisions of this post
    Revisions : Revision list
    /// The categories to which this is assigned (not stored in database)
    Categories : Category list
    /// The comments (not stored in database)
    Comments : Comment list }
with
  static member Empty =
    { Id              = "new"
      WebLogId        = ""
      AuthorId        = ""
      Status          = PostStatus.Draft
      Title           = ""
      Permalink       = ""
      PublishedOn     = int64 0
      UpdatedOn       = int64 0
      Text            = ""
      CategoryIds     = []
      Tags            = []
      PriorPermalinks = []
      Revisions       = []
      Categories      = []
      Comments        = [] }
