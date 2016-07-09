namespace myWebLog

open myWebLog.Entities
open Nancy
open Nancy.Session.Persistable

/// Levels for a user message
module Level =
  /// An informational message
  let Info = "Info"
  /// A message regarding a non-fatal but non-optimal condition
  let Warning = "WARNING"
  /// A message regarding a failure of the expected result
  let Error = "ERROR"


/// A message for the user
type UserMessage = {
  /// The level of the message (use Level module constants)
  level : string
  /// The text of the message
  message : string
  /// Further details regarding the message
  details : string option
  }
with
  /// An empty message
  static member empty =
    { level   = Level.Info
      message = ""
      details = None }


/// Parent view model for all myWebLog views
type MyWebLogModel(ctx : NancyContext, webLog : WebLog) =
  
  /// Get the messages from the session
  let getMessages () =
    let msg = ctx.Request.PersistableSession.GetOrDefault<UserMessage list>(Keys.Messages, List.empty)
    match List.length msg with
    | 0 -> ()
    | _ -> ctx.Request.Session.Delete Keys.Messages
    msg

  /// The web log for this request
  member this.webLog = webLog
  /// User messages
  member val messages = getMessages () with get, set
  /// The currently logged in user
  member this.user = ctx.Request.PersistableSession.GetOrDefault<User>(Keys.User, User.empty)
  /// The title of the page
  member val pageTitle = "" with get, set
  /// The request start time
  member this.requestStart = ctx.Items.[Keys.RequestStart] :?> int64
  /// Is a user authenticated for this request?
  member this.isAuthenticated = "" <> this.user.id
  /// Add a message to the output
  member this.addMessage message = this.messages <- message :: this.messages


/// Model for all page-of-posts pages
type PostsModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)

  /// The posts to display
  member val posts = List.empty<Post> with get, set

  /// The page number of the post list
  member val pageNbr = 0 with get, set

  /// Whether there is a newer page of posts for the list
  member val hasNewer = false with get, set

  /// Whether there is an older page of posts for the list
  member val hasOlder = true with get, set

  /// The prefix for the next/prior links
  member val urlPrefix = "" with get, set

  /// The link for the next newer page of posts
  member this.newerLink =
    match this.urlPrefix = "/posts" && this.pageNbr = 2 && this.webLog.defaultPage = "posts" with
    | true -> "/"
    | _    -> sprintf "%s/page/%i" this.urlPrefix (this.pageNbr - 1)

  /// The link for the prior (older) page of posts
  member this.olderLink = sprintf "%s/page/%i" this.urlPrefix (this.pageNbr + 1)


/// Model for page display
type PageModel(ctx, webLog, page) =
  inherit MyWebLogModel(ctx, webLog)

  /// The page to be displayed
  member this.page : Page = page
