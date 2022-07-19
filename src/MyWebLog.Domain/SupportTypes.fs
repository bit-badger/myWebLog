namespace MyWebLog

open System

/// Support functions for domain definition
[<AutoOpen>]
module private Helpers =

    /// Create a new ID (short GUID)
    // https://www.madskristensen.net/blog/A-shorter-and-URL-friendly-GUID
    let newId () =
        Convert.ToBase64String(Guid.NewGuid().ToByteArray ()).Replace('/', '_').Replace('+', '-').Substring (0, 22)


/// A user's access level
type AccessLevel =
    /// The user may create and publish posts and edit the ones they have created
    | Author
    /// The user may edit posts they did not create, but may not delete them
    | Editor
    /// The user may delete posts and configure web log settings
    | WebLogAdmin
    /// The user may manage themes (which affects all web logs for an installation)
    | Administrator

/// Functions to support access levels
module AccessLevel =
    
    /// Weightings for access levels
    let private weights =
        [ Author,        10
          Editor,        20
          WebLogAdmin,   30
          Administrator, 40
        ]
        |> Map.ofList
    
    /// Convert an access level to its string representation
    let toString =
        function
        | Author        -> "Author"
        | Editor        -> "Editor"
        | WebLogAdmin   -> "WebLogAdmin"
        | Administrator -> "Administrator"
    
    /// Parse an access level from its string representation
    let parse it =
        match it with
        | "Author"        -> Author
        | "Editor"        -> Editor
        | "WebLogAdmin"   -> WebLogAdmin
        | "Administrator" -> Administrator
        | _               -> invalidOp $"{it} is not a valid access level"
    
    /// Does a given access level allow an action that requires a certain access level?
    let hasAccess needed held =
        weights[needed] <= weights[held]


/// An identifier for a category
type CategoryId = CategoryId of string

/// Functions to support category IDs
module CategoryId =
    
    /// An empty category ID
    let empty = CategoryId ""

    /// Convert a category ID to a string
    let toString = function CategoryId ci -> ci
    
    /// Create a new category ID
    let create () = CategoryId (newId ())


/// An identifier for a comment
type CommentId = CommentId of string

/// Functions to support comment IDs
module CommentId =
    
    /// An empty comment ID
    let empty = CommentId ""

    /// Convert a comment ID to a string
    let toString = function CommentId ci -> ci
    
    /// Create a new comment ID
    let create () = CommentId (newId ())


/// Statuses for post comments
type CommentStatus =
    /// The comment is approved
    | Approved
    /// The comment has yet to be approved
    | Pending
    /// The comment was unsolicited and unwelcome
    | Spam

/// Functions to support post comment statuses
module CommentStatus =
    
    /// Convert a comment status to a string
    let toString = function Approved -> "Approved" | Pending -> "Pending" | Spam -> "Spam"
    
    /// Parse a string into a comment status
    let parse value =
        match value with
        | "Approved" -> Approved
        | "Pending"  -> Pending
        | "Spam"     -> Spam
        | it         -> invalidOp $"{it} is not a valid post status"


/// Valid values for the iTunes explicit rating
type ExplicitRating =
    | Yes
    | No
    | Clean

/// Functions to support iTunes explicit ratings
module ExplicitRating =
    /// Convert an explicit rating to a string
    let toString : ExplicitRating -> string =
        function
        | Yes   -> "yes"
        | No    -> "no"
        | Clean -> "clean"
    
    /// Parse a string into an explicit rating
    let parse : string -> ExplicitRating =
        function
        | "yes"   -> Yes
        | "no"    -> No
        | "clean" -> Clean
        | x       -> raise (invalidArg "rating" $"{x} is not a valid explicit rating")


/// A podcast episode
type Episode =
    {   /// The URL to the media file for the episode (may be permalink)
        Media : string
        
        /// The length of the media file, in bytes
        Length : int64
        
        /// The duration of the episode
        Duration : TimeSpan option
        
        /// The media type of the file (overrides podcast default if present)
        MediaType : string option
        
        /// The URL to the image file for this episode (overrides podcast image if present, may be permalink)
        ImageUrl : string option
        
        /// A subtitle for this episode
        Subtitle : string option
        
        /// This episode's explicit rating (overrides podcast rating if present)
        Explicit : ExplicitRating option
        
        /// A link to a chapter file
        ChapterFile : string option
        
        /// The MIME type for the chapter file
        ChapterType : string option
        
        /// The URL for the transcript of the episode (may be permalink)
        TranscriptUrl : string option
        
        /// The MIME type of the transcript
        TranscriptType : string option
        
        /// The language in which the transcript is written
        TranscriptLang : string option
        
        /// If true, the transcript will be declared (in the feed) to be a captions file
        TranscriptCaptions : bool option
        
        /// The season number (for serialized podcasts)
        SeasonNumber : int option
        
        /// A description of the season
        SeasonDescription : string option
        
        /// The episode number
        EpisodeNumber : double option
        
        /// A description of the episode
        EpisodeDescription : string option
    }

/// Functions to support episodes
module Episode =
    
    /// An empty episode
    let empty = {
        Media              = ""
        Length             = 0L
        Duration           = None
        MediaType          = None
        ImageUrl           = None
        Subtitle           = None
        Explicit           = None
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


open Markdig
open Markdown.ColorCode

/// Types of markup text
type MarkupText =
    /// Markdown text
    | Markdown of string
    /// HTML text
    | Html of string

/// Functions to support markup text
module MarkupText =
    
    /// Pipeline with most extensions enabled
    let private _pipeline = MarkdownPipelineBuilder().UseSmartyPants().UseAdvancedExtensions().UseColorCode().Build ()

    /// Get the source type for the markup text
    let sourceType = function Markdown _ -> "Markdown" | Html _ -> "HTML"
    
    /// Get the raw text, regardless of type
    let text = function Markdown text -> text | Html text -> text
    
    /// Get the string representation of the markup text
    let toString it = $"{sourceType it}: {text it}"
    
    /// Get the HTML representation of the markup text
    let toHtml = function Markdown text -> Markdown.ToHtml (text, _pipeline) | Html text -> text
    
    /// Parse a string into a MarkupText instance
    let parse (it : string) =
        match it with
        | text when text.StartsWith "Markdown: " -> Markdown (text.Substring 10)
        | text when text.StartsWith "HTML: " -> Html (text.Substring 6)
        | text -> invalidOp $"Cannot derive type of text ({text})"


/// An item of metadata
[<CLIMutable; NoComparison; NoEquality>]
type MetaItem =
    {   /// The name of the metadata value
        Name : string
        
        /// The metadata value
        Value : string
    }

/// Functions to support metadata items
module MetaItem =

    /// An empty metadata item
    let empty =
        { Name = ""; Value = "" }

        
/// A revision of a page or post
[<CLIMutable; NoComparison; NoEquality>]
type Revision =
    {   /// When this revision was saved
        AsOf : DateTime

        /// The text of the revision
        Text : MarkupText
    }

/// Functions to support revisions
module Revision =
    
    /// An empty revision
    let empty =
        { AsOf = DateTime.UtcNow
          Text = Html ""
        }


/// A permanent link
type Permalink = Permalink of string

/// Functions to support permalinks
module Permalink =
    
    /// An empty permalink
    let empty = Permalink ""

    /// Convert a permalink to a string
    let toString = function Permalink p -> p


/// An identifier for a page
type PageId = PageId of string

/// Functions to support page IDs
module PageId =
    
    /// An empty page ID
    let empty = PageId ""

    /// Convert a page ID to a string
    let toString = function PageId pi -> pi
    
    /// Create a new page ID
    let create () = PageId (newId ())


/// PodcastIndex.org podcast:medium allowed values
type PodcastMedium =
    | Podcast
    | Music
    | Video
    | Film
    | Audiobook
    | Newsletter
    | Blog

/// Functions to support podcast medium
module PodcastMedium =
    
    /// Convert a podcast medium to a string
    let toString =
        function
        | Podcast    -> "podcast"
        | Music      -> "music"
        | Video      -> "video"
        | Film       -> "film"
        | Audiobook  -> "audiobook"
        | Newsletter -> "newsletter"
        | Blog       -> "blog"
    
    /// Parse a string into a podcast medium
    let parse value =
        match value with
        | "podcast"    -> Podcast
        | "music"      -> Music
        | "video"      -> Video
        | "film"       -> Film
        | "audiobook"  -> Audiobook
        | "newsletter" -> Newsletter
        | "blog"       -> Blog
        | it           -> invalidOp $"{it} is not a valid podcast medium"


/// Statuses for posts
type PostStatus =
    /// The post should not be publicly available
    | Draft
    /// The post is publicly viewable
    | Published

/// Functions to support post statuses
module PostStatus =
    
    /// Convert a post status to a string
    let toString = function Draft -> "Draft" | Published -> "Published"
    
    /// Parse a string into a post status
    let parse value =
        match value with
        | "Draft" -> Draft
        | "Published" -> Published
        | it          -> invalidOp $"{it} is not a valid post status"


/// An identifier for a post
type PostId = PostId of string

/// Functions to support post IDs
module PostId =
    
    /// An empty post ID
    let empty = PostId ""

    /// Convert a post ID to a string
    let toString = function PostId pi -> pi
    
    /// Create a new post ID
    let create () = PostId (newId ())


/// An identifier for a custom feed
type CustomFeedId = CustomFeedId of string

/// Functions to support custom feed IDs
module CustomFeedId =
    
    /// An empty custom feed ID
    let empty = CustomFeedId ""

    /// Convert a custom feed ID to a string
    let toString = function CustomFeedId pi -> pi
    
    /// Create a new custom feed ID
    let create () = CustomFeedId (newId ())


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
type PodcastOptions =
    {   /// The title of the podcast
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
type CustomFeed =
    {   /// The ID of the custom feed
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
    let empty =
        { Id      = CustomFeedId ""
          Source  = Category (CategoryId "")
          Path    = Permalink ""
          Podcast = None
        }


/// Really Simple Syndication (RSS) options for this web log
[<CLIMutable; NoComparison; NoEquality>]
type RssOptions =
    {   /// Whether the site feed of posts is enabled
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
    let empty =
        { IsFeedEnabled     = true
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
    let create () = TagMapId (newId ())


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
type ThemeTemplate =
    {   /// The name of the template
        Name : string
        
        /// The text of the template
        Text : string
    }


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
        | it         -> invalidOp $"{it} is not a valid upload destination"


/// An identifier for an upload
type UploadId = UploadId of string

/// Functions to support upload IDs
module UploadId =
    
    /// An empty upload ID
    let empty = UploadId ""
    
    /// Convert an upload ID to a string
    let toString = function UploadId ui -> ui
    
    /// Create a new upload ID
    let create () = UploadId (newId ())


/// An identifier for a web log
type WebLogId = WebLogId of string

/// Functions to support web log IDs
module WebLogId =
    
    /// An empty web log ID
    let empty = WebLogId ""

    /// Convert a web log ID to a string
    let toString = function WebLogId wli -> wli
    
    /// Create a new web log ID
    let create () = WebLogId (newId ())



/// An identifier for a web log user
type WebLogUserId = WebLogUserId of string

/// Functions to support web log user IDs
module WebLogUserId =
    
    /// An empty web log user ID
    let empty = WebLogUserId ""

    /// Convert a web log user ID to a string
    let toString = function WebLogUserId wli -> wli
    
    /// Create a new web log user ID
    let create () = WebLogUserId (newId ())


