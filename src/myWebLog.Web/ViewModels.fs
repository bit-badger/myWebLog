namespace MyWebLog

open MyWebLog.Data.WebLog
open MyWebLog.Entities
open Nancy
open Nancy.Session.Persistable
open Newtonsoft.Json
open NodaTime
open NodaTime.Text
open System


/// Levels for a user message
[<RequireQualifiedAccess>]
module Level =
  /// An informational message
  let Info = "Info"
  /// A message regarding a non-fatal but non-optimal condition
  let Warning = "WARNING"
  /// A message regarding a failure of the expected result
  let Error = "ERROR"


/// A message for the user
type UserMessage = 
  { /// The level of the message (use Level module constants)
    Level : string
    /// The text of the message
    Message : string
    /// Further details regarding the message
    Details : string option }
with
  /// An empty message
  static member Empty =
    { Level   = Level.Info
      Message = ""
      Details = None }

  /// Display version
  [<JsonIgnore>]
  member this.ToDisplay =
    let classAndLabel =
      dict [
        Level.Error,   ("danger",  Resources.Error)
        Level.Warning, ("warning", Resources.Warning)
        Level.Info,    ("info",    "")
        ]
    seq {
      yield "<div class=\"alert alert-dismissable alert-"
      yield fst classAndLabel.[this.Level]
      yield "\" role=\"alert\"><button type=\"button\" class=\"close\" data-dismiss=\"alert\" aria-label=\""
      yield Resources.Close
      yield "\">&times;</button><strong>"
      match snd classAndLabel.[this.Level] with
      | ""  -> ()
      | lbl -> yield lbl.ToUpper ()
               yield " &#xbb; "
      yield this.Message
      yield "</strong>"
      match this.Details with
      | Some d -> yield "<br />"
                  yield d
      | None -> ()
      yield "</div>"
      }
    |> Seq.reduce (+)


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
    let msg = ctx.Request.PersistableSession.GetOrDefault<UserMessage list>(Keys.Messages, [])
    match List.length msg with
    | 0 -> ()
    | _ -> ctx.Request.Session.Delete Keys.Messages
    msg

  /// The web log for this request
  member this.WebLog = webLog
  /// The subtitle for the webLog (SSVE can't do IsSome that deep)
  member this.WebLogSubtitle = defaultArg this.WebLog.Subtitle ""
  /// User messages
  member val Messages = getMessages () with get, set
  /// The currently logged in user
  member this.User = ctx.Request.PersistableSession.GetOrDefault<User>(Keys.User, User.Empty)
  /// The title of the page
  member val PageTitle = "" with get, set
  /// The name and version of the application
  member this.Generator = sprintf "myWebLog %s" (ctx.Items.[Keys.Version].ToString ())
  /// The request start time
  member this.RequestStart = ctx.Items.[Keys.RequestStart] :?> int64
  /// Is a user authenticated for this request?
  member this.IsAuthenticated = "" <> this.User.Id
  /// Add a message to the output
  member this.AddMessage message = this.Messages <- message :: this.Messages

  /// Display a long date
  member this.DisplayLongDate ticks = FormatDateTime.longDate this.WebLog.TimeZone ticks
  /// Display a short date
  member this.DisplayShortDate ticks = FormatDateTime.shortDate this.WebLog.TimeZone ticks
  /// Display the time
  member this.DisplayTime ticks = FormatDateTime.time this.WebLog.TimeZone ticks
  /// The page title with the web log name appended
  member this.DisplayPageTitle =
    match this.PageTitle with
    | "" -> match this.WebLog.Subtitle with
            | Some st -> sprintf "%s | %s" this.WebLog.Name st
            | None -> this.WebLog.Name
    | pt -> sprintf "%s | %s" pt this.WebLog.Name

  /// An image with the version and load time in the tool tip
  member this.FooterLogo =
    seq {
      yield "<img src=\"/default/footer-logo.png\" alt=\"myWebLog\" title=\""
      yield sprintf "%s %s &bull; " Resources.PoweredBy this.Generator
      yield Resources.LoadedIn
      yield " "
      yield TimeSpan(System.DateTime.Now.Ticks - this.RequestStart).TotalSeconds.ToString "f3"
      yield " "
      yield Resources.Seconds.ToLower ()
      yield "\" />"
      }
    |> Seq.reduce (+)
 

// ---- Admin models ----

/// Admin Dashboard view model
type DashboardModel(ctx, webLog, counts : DashboardCounts) =
  inherit MyWebLogModel(ctx, webLog)
  /// The number of posts for the current web log
  member val Posts = counts.Posts with get, set
  /// The number of pages for the current web log
  member val Pages = counts.Pages with get, set
  /// The number of categories for the current web log
  member val Categories = counts.Categories with get, set


// ---- Category models ----

type IndentedCategory =
  { Category : Category
    Indent   : int
    Selected : bool }
with
  /// Create an indented category
  static member Create (cat : Category * int) (isSelected : string -> bool) =
    { Category = fst cat
      Indent   = snd cat
      Selected = isSelected (fst cat).Id }
  /// Display name for a category on the list page, complete with indents
  member this.ListName = sprintf "%s%s" (String.replicate this.Indent " &#xbb; &nbsp; ") this.Category.Name
  /// Display for this category as an option within a select box
  member this.Option =
    seq {
      yield sprintf "<option value=\"%s\"" this.Category.Id
      yield (match this.Selected with | true -> """ selected="selected">""" | _ -> ">")
      yield String.replicate this.Indent " &nbsp; &nbsp; "
      yield this.Category.Name
      yield "</option>"
      }
    |> String.concat ""
  /// Does the category have a description?
  member this.HasDescription = this.Category.Description.IsSome


/// Model for the list of categories
type CategoryListModel(ctx, webLog, categories) =
  inherit MyWebLogModel(ctx, webLog)
  /// The categories
  member this.Categories : IndentedCategory list = categories


/// Form for editing a category
type CategoryForm(category : Category) =
  new() = CategoryForm(Category.Empty)
  /// The name of the category
  member val Name = category.Name with get, set
  /// The slug of the category (used in category URLs)
  member val Slug = category.Slug with get, set
  /// The description of the category
  member val Description = defaultArg category.Description "" with get, set
  /// The parent category for this one
  member val ParentId = defaultArg category.ParentId "" with get, set

/// Model for editing a category
type CategoryEditModel(ctx, webLog, category) =
  inherit MyWebLogModel(ctx, webLog)
  /// The form with the category information
  member val Form = CategoryForm(category) with get, set
  /// The categories
  member val Categories : IndentedCategory list = [] with get, set


// ---- Page models ----

/// Model for page display
type PageModel(ctx, webLog, page) =
  inherit MyWebLogModel(ctx, webLog)
  /// The page to be displayed
  member this.Page : Page = page


/// Wrapper for a page with additional properties
type PageForDisplay(webLog, page) =
  /// The page
  member this.Page : Page = page
  /// The time zone of the web log
  member this.TimeZone = webLog.TimeZone
  /// The date the page was last updated
  member this.UpdatedDate = FormatDateTime.longDate this.TimeZone page.UpdatedOn
  /// The time the page was last updated
  member this.UpdatedTime = FormatDateTime.time this.TimeZone page.UpdatedOn


/// Model for page list display
type PagesModel(ctx, webLog, pages) =
  inherit MyWebLogModel(ctx, webLog)
  /// The pages
  member this.Pages : PageForDisplay list = pages


/// Form used to edit a page
type EditPageForm() =
  /// The title of the page
  member val Title = "" with get, set
  /// The link for the page
  member val Permalink = "" with get, set
  /// The source type of the revision
  member val Source = "" with get, set
  /// The text of the revision
  member val Text = "" with get, set
  /// Whether to show the page in the web log's page list
  member val ShowInPageList = false with get, set
  
  /// Fill the form with applicable values from a page
  member this.ForPage (page : Page) =
    this.Title          <- page.Title
    this.Permalink      <- page.Permalink
    this.ShowInPageList <- page.ShowInPageList
    this
  
  /// Fill the form with applicable values from a revision
  member this.ForRevision rev =
    this.Source <- rev.SourceType
    this.Text   <- rev.Text
    this


/// Model for the edit page page
type EditPageModel(ctx, webLog, page, revision) =
  inherit MyWebLogModel(ctx, webLog)
  /// The page edit form
  member val Form = EditPageForm().ForPage(page).ForRevision(revision)
  /// The page itself
  member this.Page = page
  /// The page's published date
  member this.PublishedDate = this.DisplayLongDate page.PublishedOn
  /// The page's published time
  member this.PublishedTime = this.DisplayTime page.PublishedOn
  /// The page's last updated date
  member this.LastUpdatedDate = this.DisplayLongDate page.UpdatedOn
  /// The page's last updated time
  member this.LastUpdatedTime = this.DisplayTime page.UpdatedOn
  /// Is this a new page?
  member this.IsNew = "new" = page.Id
  /// Generate a checked attribute if this page shows in the page list
  member this.PageListChecked = match page.ShowInPageList with true -> "checked=\"checked\"" | _ -> ""


// ---- Post models ----

/// Model for single post display
type PostModel(ctx, webLog, post) =
  inherit MyWebLogModel(ctx, webLog)
  /// The post being displayed
  member this.Post : Post = post
  /// The next newer post
  member val NewerPost : Post option = None with get, set
  /// The next older post
  member val OlderPost : Post option = None with get, set
  /// The date the post was published
  member this.PublishedDate = this.DisplayLongDate this.Post.PublishedOn
  /// The time the post was published
  member this.PublishedTime = this.DisplayTime this.Post.PublishedOn
  /// Does the post have tags?
  member this.HasTags = not (List.isEmpty post.Tags)
  /// Get the tags sorted
  member this.Tags = post.Tags
                     |> List.sort
                     |> List.map (fun tag -> tag, tag.Replace(' ', '+'))
  /// Does this post have a newer post?
  member this.HasNewer = this.NewerPost.IsSome
  /// Does this post have an older post?
  member this.HasOlder = this.OlderPost.IsSome


/// Wrapper for a post with additional properties
type PostForDisplay(webLog : WebLog, post : Post) =
  /// Turn tags into a pipe-delimited string of tags
  let pipedTags tags = tags |> List.reduce (fun acc x -> sprintf "%s | %s" acc x)
  /// The actual post
  member this.Post = post
  /// The time zone for the web log to which this post belongs
  member this.TimeZone = webLog.TimeZone
  /// The date the post was published
  member this.PublishedDate = FormatDateTime.longDate this.TimeZone this.Post.PublishedOn
  /// The time the post was published
  member this.PublishedTime = FormatDateTime.time this.TimeZone this.Post.PublishedOn
  /// Tags
  member this.Tags =
    match List.length this.Post.Tags with
    | 0 -> ""
    | 1 | 2 | 3 | 4 | 5 -> this.Post.Tags |> pipedTags
    | count -> sprintf "%s %s" (this.Post.Tags |> List.take 3 |> pipedTags)
                               (System.String.Format(Resources.andXMore, count - 3))


/// Model for all page-of-posts pages
type PostsModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The subtitle for the page
  member val Subtitle : string option = None with get, set
  /// The posts to display
  member val Posts : PostForDisplay list = [] with get, set
  /// The page number of the post list
  member val PageNbr = 0 with get, set
  /// Whether there is a newer page of posts for the list
  member val HasNewer = false with get, set
  /// Whether there is an older page of posts for the list
  member val HasOlder = true with get, set
  /// The prefix for the next/prior links
  member val UrlPrefix = "" with get, set

  /// The link for the next newer page of posts
  member this.NewerLink =
    match this.UrlPrefix = "/posts" && this.PageNbr = 2 && this.WebLog.DefaultPage = "posts" with
    | true -> "/"
    | _ -> sprintf "%s/page/%i" this.UrlPrefix (this.PageNbr - 1)

  /// The link for the prior (older) page of posts
  member this.OlderLink = sprintf "%s/page/%i" this.UrlPrefix (this.PageNbr + 1)


/// Form for editing a post
type EditPostForm() =
  /// The title of the post
  member val Title = "" with get, set
  /// The permalink for the post
  member val Permalink = "" with get, set
  /// The source type for this revision
  member val Source = "" with get, set
  /// The text
  member val Text = "" with get, set
  /// Tags for the post
  member val Tags = "" with get, set
  /// The selected category Ids for the post
  member val Categories : string[] = [||] with get, set
  /// Whether the post should be published
  member val PublishNow = true with get, set

  /// Fill the form with applicable values from a post
  member this.ForPost post =
    this.Title      <- post.Title
    this.Permalink  <- post.Permalink
    this.Tags       <- List.reduce (fun acc x -> sprintf "%s, %s" acc x) post.Tags
    this.Categories <- List.toArray post.CategoryIds
    this

  /// Fill the form with applicable values from a revision
  member this.ForRevision rev =
    this.Source <- rev.SourceType
    this.Text   <- rev.Text
    this

/// View model for the edit post page
type EditPostModel(ctx, webLog, post, revision) =
  inherit MyWebLogModel(ctx, webLog)

  /// The form
  member val Form = EditPostForm().ForPost(post).ForRevision(revision) with get, set
  /// The post being edited
  member val Post = post with get, set
  /// The categories to which the post may be assigned
  member val Categories : (string * string) list = [] with get, set
  /// Whether the post is currently published
  member this.IsPublished = PostStatus.Published = this.Post.Status
  /// The published date
  member this.PublishedDate = this.DisplayLongDate this.Post.PublishedOn
  /// The published time
  member this.PublishedTime = this.DisplayTime this.Post.PublishedOn


// ---- User models ----

/// Form for the log on page
type LogOnForm() =
  /// The URL to which the user will be directed upon successful log on
  member val ReturnUrl = "" with get, set
  /// The e-mail address
  member val Email = "" with get, set
  /// The user's passwor
  member val Password = "" with get, set


/// Model to support the user log on page
type LogOnModel(ctx, webLog) =
  inherit MyWebLogModel(ctx, webLog)
  /// The log on form
  member val Form = LogOnForm() with get, set
