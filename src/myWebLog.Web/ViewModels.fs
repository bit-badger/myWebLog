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
  /// The subtitle for the webLog (SSVE can't do IsSome that deep)
  member this.webLogSubtitle = defaultArg this.webLog.subtitle ""
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


// ---- Category models ----

type IndentedCategory = {
  category : Category
  indent   : int
  selected : bool
  }
with
  /// Create an indented category
  static member create (cat : Category * int) (isSelected : string -> bool) =
    { category = fst cat
      indent   = snd cat
      selected = isSelected (fst cat).id }
  /// Display name for a category on the list page, complete with indents
  member this.listName = sprintf "%s%s" (String.replicate this.indent " &#xabb; &nbsp; ") this.category.name
  /// Display for this category as an option within a select box
  member this.option =
    seq {
      yield sprintf "<option value=\"%s\"" this.category.id
      yield (match this.selected with | true -> """ selected="selected">""" | _ -> ">")
      yield String.replicate this.indent " &nbsp; &nbsp; "
      yield this.category.name
      yield "</option>"
      }
    |> String.concat ""

/// Model for the list of categories
type CategoryListModel(ctx, webLog, categories) =
  inherit MyWebLogModel(ctx, webLog)
  /// The categories
  member this.categories : IndentedCategory list = categories


/// Form for editing a category
type CategoryForm(category : Category) =
  new() = CategoryForm(Category.empty)
  /// The name of the category
  member val name = category.name with get, set
  /// The slug of the category (used in category URLs)
  member val slug = category.slug with get, set
  /// The description of the category
  member val description = defaultArg category.description "" with get, set
  /// The parent category for this one
  member val parentId = defaultArg category.parentId "" with get, set

/// Model for editing a category
type CategoryEditModel(ctx, webLog, category) =
  inherit MyWebLogModel(ctx, webLog)
  /// The form with the category information
  member val form = CategoryForm(category) with get, set
  /// The categories
  member val categories : IndentedCategory list = List.empty with get, set


// ---- Page models ----

/// Model for page display
type PageModel(ctx, webLog, page) =
  inherit MyWebLogModel(ctx, webLog)

  /// The page to be displayed
  member this.page : Page = page


/// Model for page list display
type PagesModel(ctx, webLog, pages) =
  inherit MyWebLogModel(ctx, webLog)
  /// The pages
  member this.pages : Page list = pages


/// Form used to edit a page
type EditPageForm() =
  /// The title of the page
  member val title = "" with get, set
  /// The link for the page
  member val permalink = "" with get, set
  /// The source type of the revision
  member val source = "" with get, set
  /// The text of the revision
  member val text = "" with get, set
  /// Whether to show the page in the web log's page list
  member val showInPageList = false with get, set
  
  /// Fill the form with applicable values from a page
  member this.forPage (page : Page) =
    this.title          <- page.title
    this.permalink      <- page.permalink
    this.showInPageList <- page.showInPageList
    this
  
  /// Fill the form with applicable values from a revision
  member this.forRevision rev =
    this.source <- rev.sourceType
    this.text   <- rev.text
    this


/// Model for the edit page page
type EditPageModel(ctx, webLog, page, revision) =
  inherit MyWebLogModel(ctx, webLog)
  /// The page edit form
  member val form = EditPageForm().forPage(page).forRevision(revision)
  /// The page itself
  member this.page = page
  /// The page's published date
  member this.publishedDate = this.displayLongDate page.publishedOn
  /// The page's published time
  member this.publishedTime = this.displayTime page.publishedOn
  /// The page's last updated date
  member this.lastUpdatedDate = this.displayLongDate page.updatedOn
  /// The page's last updated time
  member this.lastUpdatedTime = this.displayTime page.updatedOn
  /// Is this a new page?
  member this.isNew = "new" = page.id
  /// Generate a checked attribute if this page shows in the page list
  member this.pageListChecked = match page.showInPageList with | true -> "checked=\"checked\"" | _ -> ""


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

  /// The subtitle for the page
  member val subtitle = Option<string>.None with get, set

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


// ---- User models ----

/// Model to support the user log on page
type LogOnModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The URL to which the user will be directed upon successful log on
  member val returnUrl = "" with get, set
  /// The e-mail address
  member val email = "" with get, set
  /// The user's passwor
  member val password = "" with get, set
