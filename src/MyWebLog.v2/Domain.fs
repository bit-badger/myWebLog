namespace MyWebLog.Domain

// -- Supporting Types --

/// Types of markup text supported
type MarkupText =
  /// Text in Markdown format
  | Markdown of string
  /// Text in HTML format
  | Html of string

/// Functions to support maniuplating markup text
module MarkupText =
  /// Get the string representation of this markup text
  let toString it =
    match it with
    | Markdown x -> "Markdown", x
    | Html x     -> "HTML", x
    ||> sprintf "%s: %s"
  /// Get the HTML value of the text
  let toHtml = function
  | Markdown it -> sprintf "TODO: convert to HTML - %s" it
  | Html it -> it
  /// Parse a string representation to markup text
  let ofString (it : string) =
    match true with
    | _ when it.StartsWith "Markdown: " -> it.Substring 10 |> Markdown
    | _ when it.StartsWith "HTML: "     -> it.Substring  6 |> Html
    | _ -> sprintf "Cannot determine text type - %s" it    |> invalidOp


/// Authorization levels
type AuthorizationLevel =
  /// Authorization to administer a weblog
  | Administrator
  /// Authorization to comment on a weblog
  | User

/// Functions to support authorization levels
module AuthorizationLevel =
  /// Get the string reprsentation of an authorization level
  let toString = function Administrator -> "Administrator" | User -> "User"
  /// Create an authorization level from a string
  let ofString it =
    match it with
    | "Administrator" -> Administrator
    | "User"          -> User
    | _ -> sprintf "%s is not an authorization level" it |> invalidOp


/// Post statuses
type PostStatus =
  /// Post has not been released for public consumption
  | Draft
  /// Post is released
  | Published

/// Functions to support post statuses
module PostStatus =
  /// Get the string representation of a post status
  let toString = function Draft -> "Draft" | Published -> "Published"
  /// Create a post status from a string
  let ofString it =
    match it with
    | "Draft"     -> Draft
    | "Published" -> Published
    | _ -> sprintf "%s is not a post status" it |> invalidOp


/// Comment statuses
type CommentStatus =
  /// Comment is approved
  | Approved
  /// Comment has yet to be approved
  | Pending
  /// Comment was flagged as spam
  | Spam

/// Functions to support comment statuses
module CommentStatus =
  /// Get the string representation of a comment status
  let toString = function Approved -> "Approved" | Pending -> "Pending" | Spam -> "Spam"
  /// Create a comment status from a string
  let ofString it =
    match it with
    | "Approved" -> Approved
    | "Pending"  -> Pending
    | "Spam"     -> Spam
    | _ -> sprintf "%s is not a comment status" it |> invalidOp


/// Seconds since the Unix epoch
type UnixSeconds = UnixSeconds of int64

/// Functions to support Unix seconds
module UnixSeconds =
  /// Get the long (int64) representation of Unix seconds
  let toLong = function UnixSeconds it -> it
  /// Zero seconds past the epoch
  let none = UnixSeconds 0L


// -- IDs --

open System

// See https://www.madskristensen.net/blog/A-shorter-and-URL-friendly-GUID for info on "short GUIDs"

/// A short GUID
type ShortGuid = ShortGuid of Guid

/// Functions to support short GUIDs
module ShortGuid =
  /// Encode a GUID into a short GUID
  let toString = function
  | ShortGuid guid ->
      Convert.ToBase64String(guid.ToByteArray ())
        .Replace("/", "_")
        .Replace("+", "-")
        .Substring (0, 22)
  /// Decode a short GUID into a GUID
  let ofString (it : string) =
    it.Replace("_", "/").Replace ("-", "+")
    |> (sprintf "%s==" >> Convert.FromBase64String >> Guid >> ShortGuid)
  /// Create a new short GUID
  let create () = (Guid.NewGuid >> ShortGuid) ()
  /// The empty short GUID
  let empty = ShortGuid Guid.Empty


/// The ID of a category
type CategoryId = CategoryId of ShortGuid

/// Functions to support category IDs
module CategoryId =
  /// Get the string representation of a page ID
  let toString = function CategoryId it -> ShortGuid.toString it
  /// Create a category ID from its string representation
  let ofString = ShortGuid.ofString >> CategoryId
  /// An empty category ID
  let empty = CategoryId ShortGuid.empty


/// The ID of a comment
type CommentId = CommentId of ShortGuid

/// Functions to support comment IDs
module CommentId =
  /// Get the string representation of a comment ID
  let toString = function CommentId it -> ShortGuid.toString it
  /// Create a comment ID from its string representation
  let ofString = ShortGuid.ofString >> CommentId
  /// An empty comment ID
  let empty = CommentId ShortGuid.empty


/// The ID of a page
type PageId = PageId of ShortGuid

/// Functions to support page IDs
module PageId =
  /// Get the string representation of a page ID
  let toString = function PageId it -> ShortGuid.toString it
  /// Create a page ID from its string representation
  let ofString = ShortGuid.ofString >> PageId
  /// An empty page ID
  let empty = PageId ShortGuid.empty


/// The ID of a post
type PostId = PostId of ShortGuid

/// Functions to support post IDs
module PostId =
  /// Get the string representation of a post ID
  let toString = function PostId it -> ShortGuid.toString it
  /// Create a post ID from its string representation
  let ofString = ShortGuid.ofString >> PostId
  /// An empty post ID
  let empty = PostId ShortGuid.empty


/// The ID of a user
type UserId = UserId of ShortGuid

/// Functions to support user IDs
module UserId =
  /// Get the string representation of a user ID
  let toString = function UserId it -> ShortGuid.toString it
  /// Create a user ID from its string representation
  let ofString = ShortGuid.ofString >> UserId
  /// An empty user ID
  let empty = UserId ShortGuid.empty


/// The ID of a web log
type WebLogId = WebLogId of ShortGuid

/// Functions to support web log IDs
module WebLogId =
  /// Get the string representation of a web log ID
  let toString = function WebLogId it -> ShortGuid.toString it
  /// Create a web log ID from its string representation
  let ofString = ShortGuid.ofString >> WebLogId
  /// An empty web log ID
  let empty = WebLogId ShortGuid.empty


// -- Domain Entities --
// fsharplint:disable RecordFieldNames

/// A revision of a post or page
type Revision = {
  /// The instant which this revision was saved
  asOf : UnixSeconds
  /// The text
  text : MarkupText
  }
with
  /// An empty revision
  static member empty =
    { asOf = UnixSeconds.none
      text = Markdown ""
      }


/// A page with static content
[<CLIMutable; NoComparison; NoEquality>]
type Page = {
  /// The Id
  id             : PageId
  /// The Id of the web log to which this page belongs
  webLogId       : WebLogId
  /// The Id of the author of this page
  authorId       : UserId
  /// The title of the page
  title          : string
  /// The link at which this page is displayed
  permalink      : string
  /// The instant this page was published
  publishedOn    : UnixSeconds
  /// The instant this page was last updated
  updatedOn      : UnixSeconds
  /// Whether this page shows as part of the web log's navigation
  showInPageList : bool
  /// The current text of the page
  text           : MarkupText
  /// Revisions of this page
  revisions      : Revision list
  }
with
  static member empty = 
    { id             = PageId.empty
      webLogId       = WebLogId.empty
      authorId       = UserId.empty
      title          = ""
      permalink      = ""
      publishedOn    = UnixSeconds.none
      updatedOn      = UnixSeconds.none
      showInPageList = false
      text           = Markdown ""
      revisions      = []
      }


/// An entry in the list of pages displayed as part of the web log (derived via query)
type PageListEntry = {
  /// The permanent link for the page
  permalink : string
  /// The title of the page
  title     : string
  }


/// A web log
[<CLIMutable; NoComparison; NoEquality>]
type WebLog = {
  /// The Id
  id          : WebLogId
  /// The name
  name        : string
  /// The subtitle
  subtitle    : string option
  /// The default page ("posts" or a page Id)
  defaultPage : string
  /// The path of the theme (within /views/themes)
  themePath   : string
  /// The URL base
  urlBase     : string
  /// The time zone in which dates/times should be displayed
  timeZone    : string
  /// A list of pages to be rendered as part of the site navigation (not stored)
  pageList    : PageListEntry list
  }
with
  /// An empty web log
  static member empty =
    { id          = WebLogId.empty
      name        = ""
      subtitle    = None
      defaultPage = ""
      themePath   = "default"
      urlBase     = ""
      timeZone    = "America/New_York"
      pageList    = []
      }


/// An authorization between a user and a web log
type Authorization = {
  /// The Id of the web log to which this authorization grants access
  webLogId : WebLogId
  /// The level of access granted by this authorization
  level    : AuthorizationLevel
}


/// A user of myWebLog
[<CLIMutable; NoComparison; NoEquality>]
type User = {
  /// The Id
  id             : UserId
  /// The user name (e-mail address)
  userName       : string
  /// The first name
  firstName      : string
  /// The last name
  lastName       : string
  /// The user's preferred name
  preferredName  : string
  /// The hash of the user's password
  passwordHash   : string
  /// The URL of the user's personal site
  url            : string option
  /// The user's authorizations
  authorizations : Authorization list
  }
with
  /// An empty user
  static member empty =
    { id             = UserId.empty
      userName       = ""
      firstName      = ""
      lastName       = ""
      preferredName  = ""
      passwordHash   = ""
      url            = None
      authorizations = []
      }

/// Functions supporting users
module User =
  /// Claims for this user
  let claims user =
    user.authorizations
    |> List.map (fun a -> sprintf "%s|%s" (WebLogId.toString a.webLogId) (AuthorizationLevel.toString a.level))
    

/// A category to which posts may be assigned
[<CLIMutable; NoComparison; NoEquality>]
type Category = {
  /// The Id
  id : CategoryId
  /// The Id of the web log to which this category belongs
  webLogId : WebLogId
  /// The displayed name
  name : string
  /// The slug (used in category URLs)
  slug : string
  /// A longer description of the category
  description : string option
  /// The parent Id of this category (if a subcategory)
  parentId : CategoryId option
  /// The categories for which this category is the parent
  children : CategoryId list
  }
with
  /// An empty category
  static member empty =
    { id          = CategoryId.empty
      webLogId    = WebLogId.empty
      name        = ""
      slug        = ""
      description = None
      parentId    = None
      children    = []
      }


/// A comment (applies to a post)
[<CLIMutable; NoComparison; NoEquality>]
type Comment = {
  /// The Id
  id          : CommentId
  /// The Id of the post to which this comment applies
  postId      : PostId
  /// The Id of the comment to which this comment is a reply
  inReplyToId : CommentId option
  /// The name of the commentor
  name        : string
  /// The e-mail address of the commentor
  email       : string
  /// The URL of the commentor's personal website
  url         : string option
  /// The status of the comment
  status      : CommentStatus
  /// The instant the comment was posted
  postedOn    : UnixSeconds
  /// The text of the comment
  text        : string
  }
with
  static member empty =
    { id          = CommentId.empty
      postId      = PostId.empty
      inReplyToId = None
      name        = ""
      email       = ""
      url         = None
      status      = Pending
      postedOn    = UnixSeconds.none
      text        = ""
      }


/// A post
[<CLIMutable; NoComparison; NoEquality>]
type Post = {
  /// The Id
  id              : PostId
  /// The Id of the web log to which this post belongs
  webLogId        : WebLogId
  /// The Id of the author of this post
  authorId        : UserId
  /// The status
  status          : PostStatus
  /// The title
  title           : string
  /// The link at which the post resides
  permalink       : string
  /// The instant on which the post was originally published
  publishedOn     : UnixSeconds
  /// The instant on which the post was last updated
  updatedOn       : UnixSeconds
  /// The text of the post
  text            : MarkupText
  /// The Ids of the categories to which this is assigned
  categoryIds     : CategoryId list
  /// The tags for the post
  tags            : string list
  /// The permalinks at which this post may have once resided
  priorPermalinks : string list
  /// Revisions of this post
  revisions       : Revision list
  /// The categories to which this is assigned (not stored in database)
  categories      : Category list
  /// The comments (not stored in database)
  comments        : Comment list
  }
with
  static member empty =
    { id              = PostId.empty
      webLogId        = WebLogId.empty
      authorId        = UserId.empty
      status          = Draft
      title           = ""
      permalink       = ""
      publishedOn     = UnixSeconds.none
      updatedOn       = UnixSeconds.none
      text            = Markdown ""
      categoryIds     = []
      tags            = []
      priorPermalinks = []
      revisions       = []
      categories      = []
      comments        = []
      }


// --- UI Support ---

/// Counts of items displayed on the admin dashboard
type DashboardCounts = {
  /// The number of pages for the web log
  pages      : int
  /// The number of pages for the web log
  posts      : int
  /// The number of categories for the web log
  categories : int
  }
