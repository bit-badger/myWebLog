namespace MyWebLog

open System
open Markdig
open NodaTime

/// Support functions for domain definition
[<AutoOpen>]
module private Helpers =

    open Markdown.ColorCode

    /// Create a new ID (short GUID)
    // https://www.madskristensen.net/blog/A-shorter-and-URL-friendly-GUID
    let newId () =
        Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('/', '_').Replace('+', '-')[..22]
    
    /// Pipeline with most extensions enabled
    let markdownPipeline = MarkdownPipelineBuilder().UseSmartyPants().UseAdvancedExtensions().UseColorCode().Build()


/// Functions to support NodaTime manipulation
module Noda =
    
    /// The clock to use when getting "now" (will make mutable for testing)
    let clock: IClock = SystemClock.Instance
    
    /// The Unix epoch
    let epoch = Instant.FromUnixTimeSeconds 0L
        
    /// Truncate an instant to remove fractional seconds
    let toSecondsPrecision (value: Instant) =
        Instant.FromUnixTimeSeconds(value.ToUnixTimeSeconds())
    
    /// The current Instant, with fractional seconds truncated
    let now =
        clock.GetCurrentInstant >> toSecondsPrecision
    
    /// Convert a date/time to an Instant with whole seconds
    let fromDateTime (dt: DateTime) =
        Instant.FromDateTimeUtc(DateTime(dt.Ticks, DateTimeKind.Utc)) |> toSecondsPrecision


/// A user's access level
[<Struct>]
type AccessLevel =
    /// The user may create and publish posts and edit the ones they have created
    | Author
    /// The user may edit posts they did not create, but may not delete them
    | Editor
    /// The user may delete posts and configure web log settings
    | WebLogAdmin
    /// The user may manage themes (which affects all web logs for an installation)
    | Administrator
    
    /// Parse an access level from its string representation
    static member Parse =
        function
        | "Author" -> Author
        | "Editor" -> Editor
        | "WebLogAdmin" -> WebLogAdmin
        | "Administrator" -> Administrator
        | it -> invalidArg "level" $"{it} is not a valid access level"

    /// The string representation of this access level
    member this.Value =
        match this with
        | Author -> "Author"
        | Editor -> "Editor"
        | WebLogAdmin -> "WebLogAdmin"
        | Administrator -> "Administrator"
    
    /// Does a given access level allow an action that requires a certain access level?
    member this.HasAccess(needed: AccessLevel) =
        // TODO: Move this to user where it seems to belong better...
        let weights =
            [   Author, 10
                Editor, 20
                WebLogAdmin, 30
                Administrator, 40
            ]
            |> Map.ofList
        weights[needed] <= weights[this]


/// An identifier for a category
[<Struct>]
type CategoryId =
    | CategoryId of string
    
    /// An empty category ID
    static member Empty = CategoryId ""
    
    /// Create a new category ID
    static member Create =
        newId >> CategoryId

    /// The string representation of this category ID
    member this.Value =
        match this with CategoryId it -> it


/// An identifier for a comment
[<Struct>]
type CommentId =
    | CommentId of string
    
    /// An empty comment ID
    static member Empty = CommentId ""
    
    /// Create a new comment ID
    static member Create =
        newId >> CommentId

    /// The string representation of this comment ID
    member this.Value =
        match this with CommentId it -> it


/// Statuses for post comments
[<Struct>]
type CommentStatus =
    /// The comment is approved
    | Approved
    /// The comment has yet to be approved
    | Pending
    /// The comment was unsolicited and unwelcome
    | Spam

    /// Parse a string into a comment status
    static member Parse =
        function
        | "Approved" -> Approved
        | "Pending" -> Pending
        | "Spam" -> Spam
        | it -> invalidArg "status" $"{it} is not a valid comment status"

    /// Convert a comment status to a string
    member this.Value =
        match this with Approved -> "Approved" | Pending -> "Pending" | Spam -> "Spam"


/// Valid values for the iTunes explicit rating
[<Struct>]
type ExplicitRating =
    | Yes
    | No
    | Clean
    
    /// Parse a string into an explicit rating
    static member Parse =
        function
        | "yes" -> Yes
        | "no" -> No
        | "clean" -> Clean
        | it -> invalidArg "rating" $"{it} is not a valid explicit rating"
    
    /// The string value of this rating
    member this.Value =
        match this with Yes -> "yes" | No -> "no" | Clean -> "clean"


/// A location (specified by Podcast Index)
type Location = {
    /// The name of the location (free-form text)
    Name: string

    /// A geographic coordinate string (RFC 5870)
    Geo: string option

    /// An OpenStreetMap query
    Osm: string option
}


/// A chapter in a podcast episode
type Chapter = {
    /// The start time for the chapter
    StartTime: Duration

    /// The title for this chapter
    Title: string option

    /// A URL for an image for this chapter
    ImageUrl: string option

    /// Whether this chapter is hidden
    IsHidden: bool option

    /// The episode end time for the chapter
    EndTime: Duration option

    /// A location that applies to a chapter
    Location: Location option
}


open NodaTime.Text

/// A podcast episode
type Episode = {
    /// The URL to the media file for the episode (may be permalink)
    Media: string
    
    /// The length of the media file, in bytes
    Length: int64
    
    /// The duration of the episode
    Duration: Duration option
    
    /// The media type of the file (overrides podcast default if present)
    MediaType: string option
    
    /// The URL to the image file for this episode (overrides podcast image if present, may be permalink)
    ImageUrl: string option
    
    /// A subtitle for this episode
    Subtitle: string option
    
    /// This episode's explicit rating (overrides podcast rating if present)
    Explicit: ExplicitRating option
    
    /// Chapters for this episode
    Chapters: Chapter list option

    /// A link to a chapter file
    ChapterFile: string option
    
    /// The MIME type for the chapter file
    ChapterType: string option
    
    /// The URL for the transcript of the episode (may be permalink)
    TranscriptUrl: string option
    
    /// The MIME type of the transcript
    TranscriptType: string option
    
    /// The language in which the transcript is written
    TranscriptLang: string option
    
    /// If true, the transcript will be declared (in the feed) to be a captions file
    TranscriptCaptions: bool option
    
    /// The season number (for serialized podcasts)
    SeasonNumber: int option
    
    /// A description of the season
    SeasonDescription: string option
    
    /// The episode number
    EpisodeNumber: double option
    
    /// A description of the episode
    EpisodeDescription: string option
} with
    
    /// An empty episode
    static member Empty = {
        Media              = ""
        Length             = 0L
        Duration           = None
        MediaType          = None
        ImageUrl           = None
        Subtitle           = None
        Explicit           = None
        Chapters           = None
        ChapterFile        = None
        ChapterType        = None
        TranscriptUrl      = None
        TranscriptType     = None
        TranscriptLang     = None
        TranscriptCaptions = None
        SeasonNumber       = None
        SeasonDescription  = None
        EpisodeNumber      = None
        EpisodeDescription = None
    }
    
    /// Format a duration for an episode
    member this.FormatDuration() =
        this.Duration |> Option.map (DurationPattern.CreateWithInvariantCulture("H:mm:ss").Format)


/// Types of markup text
type MarkupText =
    /// Markdown text
    | Markdown of string
    /// HTML text
    | Html of string

    /// Parse a string into a MarkupText instance
    static member Parse(it: string) =
        match it with
        | text when text.StartsWith "Markdown: " -> Markdown text[10..]
        | text when text.StartsWith "HTML: " -> Html text[6..]
        | text -> invalidOp $"Cannot derive type of text ({text})"
    
    /// The source type for the markup text
    member this.SourceType =
        match this with Markdown _ -> "Markdown" | Html _ -> "HTML"

    /// The raw text, regardless of type
    member this.Text =
        match this with Markdown text -> text | Html text -> text
    
    /// The string representation of the markup text
    member this.Value = $"{this.SourceType}: {this.Text}"
    
    /// The HTML representation of the markup text
    member this.AsHtml() =
        match this with Markdown text -> Markdown.ToHtml(text, markdownPipeline) | Html text -> text


/// An item of metadata
[<CLIMutable; NoComparison; NoEquality>]
type MetaItem = {
    /// The name of the metadata value
    Name : string
    
    /// The metadata value
    Value : string
} with
    
    /// An empty metadata item
    static member Empty =
        { Name = ""; Value = "" }


/// A revision of a page or post
[<CLIMutable; NoComparison; NoEquality>]
type Revision = {
    /// When this revision was saved
    AsOf : Instant

    /// The text of the revision
    Text : MarkupText
} with
    
    /// An empty revision
    static member Empty =
        { AsOf = Noda.epoch; Text = Html "" }


/// A permanent link
[<Struct>]
type Permalink =
    | Permalink of string

    /// An empty permalink
    static member Empty = Permalink ""
    
    /// The string value of this permalink
    member this.Value =
        match this with Permalink it -> it


/// An identifier for a page
[<Struct>]
type PageId =
    | PageId of string

    /// An empty page ID
    static member Empty = PageId ""
    
    /// Create a new page ID
    static member Create =
        newId >> PageId
    
    /// The string value of this page ID
    member this.Value =
        match this with PageId it -> it


/// PodcastIndex.org podcast:medium allowed values
[<Struct>]
type PodcastMedium =
    | Podcast
    | Music
    | Video
    | Film
    | Audiobook
    | Newsletter
    | Blog

    /// Parse a string into a podcast medium
    static member Parse =
        function
        | "podcast" -> Podcast
        | "music" -> Music
        | "video" -> Video
        | "film" -> Film
        | "audiobook" -> Audiobook
        | "newsletter" -> Newsletter
        | "blog" -> Blog
        | it -> invalidArg "medium" $"{it} is not a valid podcast medium"
    
    /// The string value of this podcast medium
    member this.Value =
        match this with
        | Podcast -> "podcast"
        | Music -> "music"
        | Video -> "video"
        | Film -> "film"
        | Audiobook -> "audiobook"
        | Newsletter -> "newsletter"
        | Blog -> "blog"


/// Statuses for posts
[<Struct>]
type PostStatus =
    /// The post should not be publicly available
    | Draft
    /// The post is publicly viewable
    | Published

    /// Parse a string into a post status
    static member Parse =
        function
        | "Draft" -> Draft
        | "Published" -> Published
        | it -> invalidArg "status" $"{it} is not a valid post status"
    
    /// The string representation of this post status
    member this.Value =
        match this with Draft -> "Draft" | Published -> "Published"


/// An identifier for a post
[<Struct>]
type PostId =
    | PostId of string

    /// An empty post ID
    static member Empty = PostId ""
    
    /// Create a new post ID
    static member Create =
        newId >> PostId
    
    /// Convert a post ID to a string
    member this.Value =
        match this with PostId it -> it


/// A redirection for a previously valid URL
[<CLIMutable; NoComparison; NoEquality>]
type RedirectRule = {
    /// The From string or pattern
    From: string
    
    /// The To string or pattern
    To: string
    
    /// Whether to use regular expressions on this rule
    IsRegex: bool
} with
    
    /// An empty redirect rule
    static member Empty = {
        From    = ""
        To      = ""
        IsRegex = false
    }


/// An identifier for a custom feed
type CustomFeedId = CustomFeedId of string

/// Functions to support custom feed IDs
module CustomFeedId =
    
    /// An empty custom feed ID
    let empty = CustomFeedId ""

    /// Convert a custom feed ID to a string
    let toString = function CustomFeedId pi -> pi
    
    /// Create a new custom feed ID
    let create = newId >> CustomFeedId


/// The source for a custom feed
type CustomFeedSource =
    /// A feed based on a particular category
    | Category of CategoryId
    /// A feed based on a particular tag
    | Tag of string

/// Functions to support feed sources
module CustomFeedSource =
    /// Create a string version of a feed source
    let toString : CustomFeedSource -> string =
        function
        | Category (CategoryId catId) -> $"category:{catId}"
        | Tag tag -> $"tag:{tag}"
    
    /// Parse a feed source from its string version
    let parse : string -> CustomFeedSource =
        let value (it : string) = it.Split(":").[1]
        function
        | source when source.StartsWith "category:" -> (value >> CategoryId >> Category) source
        | source when source.StartsWith "tag:"      -> (value >> Tag) source
        | source -> invalidArg "feedSource" $"{source} is not a valid feed source"


/// Options for a feed that describes a podcast
type PodcastOptions = {
    /// The title of the podcast
    Title : string
    
    /// A subtitle for the podcast
    Subtitle : string option
    
    /// The number of items in the podcast feed
    ItemsInFeed : int
    
    /// A summary of the podcast (iTunes field)
    Summary : string
    
    /// The display name of the podcast author (iTunes field)
    DisplayedAuthor : string
    
    /// The e-mail address of the user who registered the podcast at iTunes
    Email : string
    
    /// The link to the image for the podcast
    ImageUrl : Permalink
    
    /// The category from Apple Podcasts (iTunes) under which this podcast is categorized
    AppleCategory : string
    
    /// A further refinement of the categorization of this podcast (Apple Podcasts/iTunes field / values)
    AppleSubcategory : string option
    
    /// The explictness rating (iTunes field)
    Explicit : ExplicitRating
    
    /// The default media type for files in this podcast
    DefaultMediaType : string option
    
    /// The base URL for relative URL media files for this podcast (optional; defaults to web log base)
    MediaBaseUrl : string option
    
    /// A GUID for this podcast
    PodcastGuid : Guid option
    
    /// A URL at which information on supporting the podcast may be found (supports permalinks)
    FundingUrl : string option
    
    /// The text to be displayed in the funding item within the feed
    FundingText : string option
    
    /// The medium (what the podcast IS, not what it is ABOUT)
    Medium : PodcastMedium option
}


/// A custom feed
type CustomFeed = {
    /// The ID of the custom feed
    Id : CustomFeedId
    
    /// The source for the custom feed
    Source : CustomFeedSource
    
    /// The path for the custom feed
    Path : Permalink
    
    /// Podcast options, if the feed defines a podcast
    Podcast : PodcastOptions option
}

/// Functions to support custom feeds
module CustomFeed =
    
    /// An empty custom feed
    let empty = {
        Id      = CustomFeedId ""
        Source  = Category (CategoryId "")
        Path    = Permalink ""
        Podcast = None
    }


/// Really Simple Syndication (RSS) options for this web log
[<CLIMutable; NoComparison; NoEquality>]
type RssOptions = {
    /// Whether the site feed of posts is enabled
    IsFeedEnabled : bool
    
    /// The name of the file generated for the site feed
    FeedName : string
    
    /// Override the "posts per page" setting for the site feed
    ItemsInFeed : int option
    
    /// Whether feeds are enabled for all categories
    IsCategoryEnabled : bool
    
    /// Whether feeds are enabled for all tags
    IsTagEnabled : bool
    
    /// A copyright string to be placed in all feeds
    Copyright : string option
    
    /// Custom feeds for this web log
    CustomFeeds: CustomFeed list
}

/// Functions to support RSS options
module RssOptions =
    
    /// An empty set of RSS options
    let empty = {
        IsFeedEnabled     = true
        FeedName          = "feed.xml"
        ItemsInFeed       = None
        IsCategoryEnabled = true
        IsTagEnabled      = true
        Copyright         = None
        CustomFeeds       = []
    }


/// An identifier for a tag mapping
type TagMapId = TagMapId of string

/// Functions to support tag mapping IDs
module TagMapId =
    
    /// An empty tag mapping ID
    let empty = TagMapId ""
    
    /// Convert a tag mapping ID to a string
    let toString = function TagMapId tmi -> tmi
    
    /// Create a new tag mapping ID
    let create = newId >> TagMapId


/// An identifier for a theme (represents its path)
type ThemeId = ThemeId of string

/// Functions to support theme IDs
module ThemeId =
    let toString = function ThemeId ti -> ti


/// An identifier for a theme asset
type ThemeAssetId = ThemeAssetId of ThemeId * string

/// Functions to support theme asset IDs
module ThemeAssetId =
    
    /// Convert a theme asset ID into a path string
    let toString =  function ThemeAssetId (ThemeId theme, asset) -> $"{theme}/{asset}"
    
    /// Convert a string into a theme asset ID
    let ofString (it : string) =
        let themeIdx = it.IndexOf "/"
        ThemeAssetId (ThemeId it[..(themeIdx - 1)], it[(themeIdx + 1)..])


/// A template for a theme
type ThemeTemplate = {
    /// The name of the template
    Name : string
    
    /// The text of the template
    Text : string
}

/// Functions to support theme templates
module ThemeTemplate =
    
    /// An empty theme template
    let empty =
        { Name = ""; Text = "" }


/// Where uploads should be placed
type UploadDestination =
    | Database
    | Disk

/// Functions to support upload destinations
module UploadDestination =
    
    /// Convert an upload destination to its string representation
    let toString = function Database -> "Database" | Disk -> "Disk"
    
    /// Parse an upload destination from its string representation
    let parse value =
        match value with
        | "Database" -> Database
        | "Disk"     -> Disk
        | it         -> invalidArg "destination" $"{it} is not a valid upload destination"


/// An identifier for an upload
type UploadId = UploadId of string

/// Functions to support upload IDs
module UploadId =
    
    /// An empty upload ID
    let empty = UploadId ""
    
    /// Convert an upload ID to a string
    let toString = function UploadId ui -> ui
    
    /// Create a new upload ID
    let create = newId >> UploadId


/// An identifier for a web log
type WebLogId = WebLogId of string

/// Functions to support web log IDs
module WebLogId =
    
    /// An empty web log ID
    let empty = WebLogId ""

    /// Convert a web log ID to a string
    let toString = function WebLogId wli -> wli
    
    /// Create a new web log ID
    let create = newId >> WebLogId



/// An identifier for a web log user
type WebLogUserId = WebLogUserId of string

/// Functions to support web log user IDs
module WebLogUserId =
    
    /// An empty web log user ID
    let empty = WebLogUserId ""

    /// Convert a web log user ID to a string
    let toString = function WebLogUserId wli -> wli
    
    /// Create a new web log user ID
    let create = newId >> WebLogUserId


