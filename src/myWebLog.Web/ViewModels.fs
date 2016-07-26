namespace myWebLog

open myWebLog.Data.WebLog
open myWebLog.Entities
open Nancy
open Nancy.Session.Persistable
open Newtonsoft.Json
open NodaTime
open NodaTime.Text
open System


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
  static member empty = {
    level   = Level.Info
    message = ""
    details = None
    }

  /// Display version
  [<JsonIgnore>]
  member this.toDisplay =
    let classAndLabel =
      dict [
        Level.Error,   ("danger",  Resources.Error)
        Level.Warning, ("warning", Resources.Warning)
        Level.Info,    ("info",    "")
        ]
    seq {
      yield "<div class=\"alert alert-dismissable alert-"
      yield fst classAndLabel.[this.level]
      yield "\" role=\"alert\"><button type=\"button\" class=\"close\" data-dismiss=\"alert\" aria-label=\""
      yield Resources.Close
      yield "\">&times;</button><strong>"
      match snd classAndLabel.[this.level] with
      | ""  -> ()
      | lbl -> yield lbl.ToUpper ()
               yield " &#xbb; "
      yield this.message
      yield "</strong>"
      match this.details with
      | Some d -> yield "<br />"
                  yield d
      | None   -> ()
      yield "</div>"
      }
    |> Seq.reduce (fun acc x -> acc + x)


/// Helpers to format local date/time using NodaTime
module FormatDateTime =
  
  /// Convert ticks to a zoned date/time
  let zonedTime timeZone ticks = Instant(ticks).InZone(DateTimeZoneProviders.Tzdb.[timeZone])

  /// Display a long date
  let longDate timeZone ticks =
    zonedTime timeZone ticks
    |> ZonedDateTimePattern.CreateWithCurrentCulture("MMMM d',' yyyy", DateTimeZoneProviders.Tzdb).Format
  
  /// Display a short date
  let shortDate timeZone ticks =
    zonedTime timeZone ticks
    |> ZonedDateTimePattern.CreateWithCurrentCulture("MMM d',' yyyy", DateTimeZoneProviders.Tzdb).Format
  
  /// Display the time
  let time timeZone ticks =
    (zonedTime timeZone ticks
     |> ZonedDateTimePattern.CreateWithCurrentCulture("h':'mmtt", DateTimeZoneProviders.Tzdb).Format).ToLower()
  

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
  /// The name and version of the application
  member this.generator = sprintf "myWebLog %s" (ctx.Items.[Keys.Version].ToString ())
  /// The request start time
  member this.requestStart = ctx.Items.[Keys.RequestStart] :?> int64
  /// Is a user authenticated for this request?
  member this.isAuthenticated = "" <> this.user.id
  /// Add a message to the output
  member this.addMessage message = this.messages <- message :: this.messages

  /// Display a long date
  member this.displayLongDate ticks = FormatDateTime.longDate this.webLog.timeZone ticks
  /// Display a short date
  member this.displayShortDate ticks = FormatDateTime.shortDate this.webLog.timeZone ticks
  /// Display the time
  member this.displayTime ticks = FormatDateTime.time this.webLog.timeZone ticks
  /// The page title with the web log name appended
  member this.displayPageTitle =
    match this.pageTitle with
    | "" -> match this.webLog.subtitle with
            | Some st -> sprintf "%s | %s" this.webLog.name st
            | None    -> this.webLog.name
    | pt -> sprintf "%s | %s" pt this.webLog.name

  /// An image with the version and load time in the tool tip
  member this.footerLogo =
    seq {
      yield "<img src=\"/default/footer-logo.png\" alt=\"myWebLog\" title=\""
      yield sprintf "%s %s &bull; " Resources.PoweredBy this.generator
      yield Resources.LoadedIn
      yield " "
      yield TimeSpan(System.DateTime.Now.Ticks - this.requestStart).TotalSeconds.ToString "f3"
      yield " "
      yield Resources.Seconds.ToLower ()
      yield "\" />"
      }
    |> Seq.reduce (fun acc x -> acc + x)
 

// ---- Admin models ----

/// Admin Dashboard view model
type DashboardModel(ctx, webLog, counts : DashboardCounts) =
  inherit MyWebLogModel(ctx, webLog)
  /// The number of posts for the current web log
  member val posts = counts.posts with get, set
  /// The number of pages for the current web log
  member val pages = counts.pages with get, set
  /// The number of categories for the current web log
  member val categories = counts.categories with get, set


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
  member this.listName = sprintf "%s%s" (String.replicate this.indent " &#xbb; &nbsp; ") this.category.name
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
  /// Does the category have a description?
  member this.hasDescription = this.category.description.IsSome


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


/// Wrapper for a page with additional properties
type PageForDisplay(webLog, page) =
  /// The page
  member this.page : Page = page
  /// The time zone of the web log
  member this.timeZone = webLog.timeZone
  /// The date the page was last updated
  member this.updatedDate = FormatDateTime.longDate this.timeZone page.updatedOn
  /// The time the page was last updated
  member this.updatedTime = FormatDateTime.time this.timeZone page.updatedOn


/// Model for page list display
type PagesModel(ctx, webLog, pages) =
  inherit MyWebLogModel(ctx, webLog)
  /// The pages
  member this.pages : PageForDisplay list = pages


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

/// Model for single post display
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
  /// Does this post have a newer post?
  member this.hasNewer = this.newerPost.IsSome
  /// Does this post have an older post?
  member this.hasOlder = this.olderPost.IsSome

/// Wrapper for a post with additional properties
type PostForDisplay(webLog : WebLog, post : Post) =
  /// Turn tags into a pipe-delimited string of tags
  let pipedTags tags = tags |> List.reduce (fun acc x -> sprintf "%s | %s" acc x)
  /// The actual post
  member this.post = post
  /// The time zone for the web log to which this post belongs
  member this.timeZone = webLog.timeZone
  /// The date the post was published
  member this.publishedDate = FormatDateTime.longDate this.timeZone this.post.publishedOn
  /// The time the post was published
  member this.publishedTime = FormatDateTime.time this.timeZone this.post.publishedOn
  /// Tags
  member this.tags =
    match List.length this.post.tags with
    | 0                 -> ""
    | 1 | 2 | 3 | 4 | 5 -> this.post.tags |> pipedTags
    | count             -> sprintf "%s %s" (this.post.tags |> List.take 3 |> pipedTags)
                                           (System.String.Format(Resources.andXMore, count - 3))


/// Model for all page-of-posts pages
type PostsModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The subtitle for the page
  member val subtitle = Option<string>.None with get, set
  /// The posts to display
  member val posts = List.empty<PostForDisplay> with get, set
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

/// Form for the log on page
type LogOnForm() =
  /// The URL to which the user will be directed upon successful log on
  member val returnUrl = "" with get, set
  /// The e-mail address
  member val email = "" with get, set
  /// The user's passwor
  member val password = "" with get, set


/// Model to support the user log on page
type LogOnModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The log on form
  member val form = LogOnForm() with get, set
