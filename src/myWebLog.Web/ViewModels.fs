namespace myWebLog

open myWebLog.Entities
open Nancy
open Nancy.Session.Persistable
open NodaTime
open NodaTime.Text


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

  /// Convert ticks to a zoned date/time for the current web log
  member this.zonedTime ticks = Instant(ticks).InZone(DateTimeZoneProviders.Tzdb.[this.webLog.timeZone])

  /// Display a long date
  member this.displayLongDate ticks =
    this.zonedTime ticks
    |> ZonedDateTimePattern.CreateWithCurrentCulture("MMMM d',' yyyy", DateTimeZoneProviders.Tzdb).Format
  
  /// Display a short date
  member this.displayShortDate ticks =
    this.zonedTime ticks
    |> ZonedDateTimePattern.CreateWithCurrentCulture("MMM d',' yyyy", DateTimeZoneProviders.Tzdb).Format
  
  /// Display the time
  member this.displayTime ticks =
    (this.zonedTime ticks
     |> ZonedDateTimePattern.CreateWithCurrentCulture("h':'mmtt", DateTimeZoneProviders.Tzdb).Format).ToLower()


// ---- Admin models ----

/// Admin Dashboard view model
type DashboardModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The number of posts for the current web log
  member val posts = 0 with get, set
  /// The number of pages for the current web log
  member val pages = 0 with get, set
  /// The number of categories for the current web log
  member val categories = 0 with get, set


// ---- Page models ----

/// Model for page display
type PageModel(ctx, webLog, page) =
  inherit MyWebLogModel(ctx, webLog)

  /// The page to be displayed
  member this.page : Page = page


// ---- Post models ----

/// Model for post display
type PostModel(ctx, webLog, post) =
  inherit MyWebLogModel(ctx, webLog)
  /// The post being displayed
  member this.post : Post = post
  /// The next newer post
  member val newerPost = Option<Post>.None with get, set
  /// The next older post
  member val olderPost = Option<Post>.None with get, set
  /// The date the post was published
  member this.publishedDate = this.displayLongDate this.post.publishedOn
  /// The time the post was published
  member this.publishedTime = this.displayTime this.post.publishedOn
  /// Does the post have tags?
  member this.hasTags = List.length post.tags > 0
  /// Get the tags sorted
  member this.tags = post.tags
                     |> List.sort
                     |> List.map (fun tag -> tag, tag.Replace(' ', '+'))

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


/// Form for editing a post
type EditPostForm() =
  /// The title of the post
  member val title = "" with get, set
  /// The permalink for the post
  member val permalink = "" with get, set
  /// The source type for this revision
  member val source = "" with get, set
  /// The text
  member val text = "" with get, set
  /// Tags for the post
  member val tags = "" with get, set
  /// The selected category Ids for the post
  member val categories = Array.empty<string> with get, set
  /// Whether the post should be published
  member val publishNow = true with get, set

  /// Fill the form with applicable values from a post
  member this.forPost post =
    this.title      <- post.title
    this.permalink  <- post.permalink
    this.tags       <- List.reduce (fun acc x -> sprintf "%s, %s" acc x) post.tags
    this.categories <- List.toArray post.categoryIds
    this

  /// Fill the form with applicable values from a revision
  member this.forRevision rev =
    this.source <- rev.sourceType
    this.text   <- rev.text
    this

/// View model for the edit post page
type EditPostModel(ctx, webLog, post, revision) =
  inherit MyWebLogModel(ctx, webLog)

  /// The form
  member val form = EditPostForm().forPost(post).forRevision(revision) with get, set
  /// The post being edited
  member val post = post with get, set
  /// The categories to which the post may be assigned
  member val categories = List.empty<string * string> with get, set
  /// Whether the post is currently published
  member this.isPublished = PostStatus.Published = this.post.status
  /// The published date
  member this.publishedDate = this.displayLongDate this.post.publishedOn
  /// The published time
  member this.publishedTime = this.displayTime this.post.publishedOn
