namespace MyWebLog.ViewModels

open System
open MyWebLog
open NodaTime
open NodaTime.Text

/// Helper functions for view models
[<AutoOpen>]
module private Helpers =
    
    /// Create a string option if a string is blank
    let noneIfBlank it =
        match (defaultArg (Option.ofObj it) "").Trim() with "" -> None | trimmed -> Some trimmed


/// Helper functions that are needed outside this file
[<AutoOpen>]
module PublicHelpers =
    
    /// If the web log is not being served from the domain root, add the path information to relative URLs in page and
    /// post text
    let addBaseToRelativeUrls extra (text: string) =
        if extra = "" then text
        else
            text.Replace("href=\"/", $"href=\"{extra}/").Replace("href=/", $"href={extra}/")
                .Replace("src=\"/", $"src=\"{extra}/").Replace("src=/", $"src={extra}/")


/// The model used to display the admin dashboard
[<NoComparison; NoEquality>]
type DashboardModel = {
    /// The number of published posts
    Posts: int

    /// The number of post drafts
    Drafts: int

    /// The number of pages
    Pages: int

    /// The number of pages in the page list
    ListedPages: int

    /// The number of categories
    Categories: int

    /// The top-level categories
    TopLevelCategories: int
}


/// Details about a category, used to display category lists
[<NoComparison; NoEquality>]
type DisplayCategory = {
    /// The ID of the category
    Id: string
    
    /// The slug for the category
    Slug: string
    
    /// The name of the category
    Name: string
    
    /// A description of the category
    Description: string option
    
    /// The parent category names for this (sub)category
    ParentNames: string array
    
    /// The number of posts in this category
    PostCount: int
}


/// A display version of an episode chapter
type DisplayChapter = {
    /// The start time of the chapter (H:mm:ss.FF format)
    StartTime: string
    
    /// The title of the chapter
    Title: string
    
    /// An image to display for this chapter
    ImageUrl: string
    
    /// A URL with information about this chapter
    Url: string
    
    /// Whether this chapter should be displayed in podcast players
    IsHidden: bool
    
    /// The end time of the chapter (H:mm:ss.FF format)
    EndTime: string
    
    /// The name of a location
    LocationName: string
    
    /// The geographic coordinates of the location
    LocationGeo: string
    
    /// An OpenStreetMap query for this location
    LocationOsm: string
} with

    /// Create a display chapter from a chapter
    static member FromChapter (chapter: Chapter) =
        let pattern = DurationPattern.CreateWithInvariantCulture "H:mm:ss.FF"
        { StartTime    = pattern.Format chapter.StartTime
          Title        = defaultArg chapter.Title ""
          ImageUrl     = defaultArg chapter.ImageUrl ""
          Url          = defaultArg chapter.Url ""
          IsHidden     = defaultArg chapter.IsHidden false
          EndTime      = chapter.EndTime  |> Option.map pattern.Format          |> Option.defaultValue ""
          LocationName = chapter.Location |> Option.map _.Name                  |> Option.defaultValue ""
          LocationGeo  = chapter.Location |> Option.map _.Geo                   |> Option.defaultValue ""
          LocationOsm  = chapter.Location |> Option.map _.Osm |> Option.flatten |> Option.defaultValue "" }


/// A display version of a custom feed definition
type DisplayCustomFeed = {
    /// The ID of the custom feed
    Id: string
    
    /// The source of the custom feed
    Source: string
    
    /// The relative path at which the custom feed is served
    Path: string
    
    /// Whether this custom feed is for a podcast
    IsPodcast: bool
} with
    
    /// Create a display version from a custom feed
    static member FromFeed (cats: DisplayCategory array) (feed: CustomFeed) =
        let source =
            match feed.Source with
            | Category (CategoryId catId) ->
                cats
                |> Array.tryFind (fun cat -> cat.Id = catId)
                |> Option.map _.Name
                |> Option.defaultValue "--INVALID; DELETE THIS FEED--"
                |> sprintf "Category: %s"
            | Tag tag -> $"Tag: {tag}"
        { Id        = string feed.Id
          Source    = source
          Path      = string feed.Path
          IsPodcast = Option.isSome feed.Podcast }


/// Details about a page used to display page lists
[<NoComparison; NoEquality>]
type DisplayPage = {
    /// The ID of this page
    Id: string

    /// The ID of the author of this page
    AuthorId: string
    
    /// The title of the page
    Title: string

    /// The link at which this page is displayed
    Permalink: string

    /// When this page was published
    PublishedOn: DateTime

    /// When this page was last updated
    UpdatedOn: DateTime

    /// Whether this page shows as part of the web log's navigation
    IsInPageList: bool
    
    /// Is this the default page?
    IsDefault: bool
    
    /// The text of the page
    Text: string
    
    /// The metadata for the page
    Metadata: MetaItem list
} with
    
    /// Create a minimal display page (no text or metadata) from a database page
    static member FromPageMinimal (webLog: WebLog) (page: Page) =
        { Id           = string page.Id
          AuthorId     = string page.AuthorId
          Title        = page.Title
          Permalink    = string page.Permalink
          PublishedOn  = webLog.LocalTime page.PublishedOn
          UpdatedOn    = webLog.LocalTime page.UpdatedOn
          IsInPageList = page.IsInPageList
          IsDefault    = string page.Id = webLog.DefaultPage
          Text         = ""
          Metadata     = [] }
    
    /// Create a display page from a database page
    static member FromPage webLog page =
        { DisplayPage.FromPageMinimal webLog page with
            Text     = addBaseToRelativeUrls webLog.ExtraPath page.Text
            Metadata = page.Metadata
        }


open System.IO

/// Information about a theme used for display
[<NoComparison; NoEquality>]
type DisplayTheme = {
    /// The ID / path slug of the theme
    Id: string
    
    /// The name of the theme
    Name: string
    
    /// The version of the theme
    Version: string
    
    /// How many templates are contained in the theme
    TemplateCount: int
    
    /// Whether the theme is in use by any web logs
    IsInUse: bool
    
    /// Whether the theme .zip file exists on the filesystem
    IsOnDisk: bool
} with
    
    /// Create a display theme from a theme
    static member FromTheme inUseFunc (theme: Theme) =
        let fileName = if string theme.Id = "default" then "default-theme.zip" else $"./themes/{theme.Id}-theme.zip"
        { Id            = string theme.Id
          Name          = theme.Name
          Version       = theme.Version
          TemplateCount = List.length theme.Templates
          IsInUse       = inUseFunc theme.Id
          IsOnDisk      = File.Exists fileName }


/// Information about an uploaded file used for display
[<NoComparison; NoEquality>]
type DisplayUpload = {
    /// The ID of the uploaded file
    Id: string
    
    /// The name of the uploaded file
    Name: string
    
    /// The path at which the file is served
    Path: string
    
    /// The date/time the file was updated
    UpdatedOn: DateTime option
    
    /// The source for this file (created from UploadDestination DU)
    Source: string
} with
    
    /// Create a display uploaded file
    static member FromUpload (webLog: WebLog) (source: UploadDestination) (upload: Upload) =
        let path = string upload.Path
        let name = Path.GetFileName path
        { Id        = string upload.Id
          Name      = name
          Path      = path.Replace(name, "")
          UpdatedOn = Some (webLog.LocalTime upload.UpdatedOn)
          Source    = string source }


/// View model to display a user's information
[<NoComparison; NoEquality>]
type DisplayUser = {
    /// The ID of the user
    Id: string

    /// The user name (e-mail address)
    Email: string

    /// The user's first name
    FirstName: string

    /// The user's last name
    LastName: string

    /// The user's preferred name
    PreferredName: string

    /// The URL of the user's personal site
    Url: string

    /// The user's access level
    AccessLevel: string
    
    /// When the user was created
    CreatedOn: DateTime
    
    /// When the user last logged on
    LastSeenOn: Nullable<DateTime>
} with
    
    /// Construct a displayed user from a web log user
    static member FromUser (webLog: WebLog) (user: WebLogUser) =
        { Id            = string user.Id
          Email         = user.Email
          FirstName     = user.FirstName
          LastName      = user.LastName
          PreferredName = user.PreferredName
          Url           = defaultArg user.Url ""
          AccessLevel   = string user.AccessLevel
          CreatedOn     = webLog.LocalTime user.CreatedOn
          LastSeenOn    = user.LastSeenOn |> Option.map webLog.LocalTime |> Option.toNullable }


/// View model for editing categories
[<CLIMutable; NoComparison; NoEquality>]
type EditCategoryModel = {
    /// The ID of the category being edited
    CategoryId: string
    
    /// The name of the category
    Name: string
    
    /// The category's URL slug
    Slug: string
    
    /// A description of the category (optional)
    Description: string
    
    /// The ID of the category for which this is a subcategory (optional)
    ParentId: string
} with
    
    /// Create an edit model from an existing category 
    static member FromCategory (cat: Category) =
        { CategoryId  = string cat.Id
          Name        = cat.Name
          Slug        = cat.Slug
          Description = defaultArg cat.Description ""
          ParentId    = cat.ParentId |> Option.map string |> Option.defaultValue "" }
    
    /// Is this a new category?
    member this.IsNew =
        this.CategoryId = "new"


/// View model to add/edit an episode chapter
[<CLIMutable; NoComparison; NoEquality>]
type EditChapterModel = {
    /// The ID of the post to which the chapter belongs
    PostId: string
    
    /// The index in the chapter list (-1 means new)
    Index: int
    
    /// The start time of the chapter (H:mm:ss.FF format)
    StartTime: string
    
    /// The title of the chapter
    Title: string
    
    /// An image to display for this chapter
    ImageUrl: string
    
    /// A URL with information about this chapter
    Url: string
    
    /// Whether this chapter should be displayed in podcast players
    IsHidden: bool
    
    /// The end time of the chapter (HH:MM:SS.FF format)
    EndTime: string
    
    /// The name of a location
    LocationName: string
    
    /// The geographic coordinates of the location
    LocationGeo: string
    
    /// An OpenStreetMap query for this location
    LocationOsm: string
    
    /// Whether to add another chapter after adding this one
    AddAnother: bool
} with

    /// Create a display chapter from a chapter
    static member FromChapter (postId: PostId) idx chapter =
        let it = DisplayChapter.FromChapter chapter
        { PostId       = string postId
          Index        = idx
          StartTime    = it.StartTime
          Title        = it.Title
          ImageUrl     = it.ImageUrl
          Url          = it.Url
          IsHidden     = it.IsHidden
          EndTime      = it.EndTime
          LocationName = it.LocationName
          LocationGeo  = it.LocationGeo
          LocationOsm  = it.LocationOsm
          AddAnother   = false }
    
    /// Create a chapter from the values in this model
    member this.ToChapter () =
        let parseDuration name value =
            let pattern =
                match value |> Seq.fold (fun count chr -> if chr = ':' then count + 1 else count) 0 with
                | 0 -> "S"
                | 1 -> "M:ss"
                | 2 -> "H:mm:ss"
                | _ -> invalidArg name "Max time format is H:mm:ss"
                |> function
                | it -> DurationPattern.CreateWithInvariantCulture $"{it}.FFFFFFFFF"
            let result = pattern.Parse value
            if result.Success then result.Value else raise result.Exception
        let location =
            match noneIfBlank this.LocationName with
            | None -> None
            | Some name -> Some { Name = name; Geo = this.LocationGeo; Osm = noneIfBlank this.LocationOsm }
        { StartTime = parseDuration (nameof this.StartTime) this.StartTime
          Title     = noneIfBlank this.Title
          ImageUrl  = noneIfBlank this.ImageUrl
          Url       = noneIfBlank this.Url
          IsHidden  = if this.IsHidden then Some true else None
          EndTime   = noneIfBlank this.EndTime |> Option.map (parseDuration (nameof this.EndTime))
          Location  = location }


/// View model to edit a custom RSS feed
[<CLIMutable; NoComparison; NoEquality>]
type EditCustomFeedModel = {
    /// The ID of the feed being editing
    Id: string
    
    /// The type of source for this feed ("category" or "tag")
    SourceType: string
    
    /// The category ID or tag on which this feed is based
    SourceValue: string
    
    /// The relative path at which this feed is served
    Path: string
    
    /// Whether this feed defines a podcast
    IsPodcast: bool
    
    /// The title of the podcast
    Title: string
    
    /// A subtitle for the podcast
    Subtitle: string
    
    /// The number of items in the podcast feed
    ItemsInFeed: int
    
    /// A summary of the podcast (iTunes field)
    Summary: string
    
    /// The display name of the podcast author (iTunes field)
    DisplayedAuthor: string
    
    /// The e-mail address of the user who registered the podcast at iTunes
    Email: string
    
    /// The link to the image for the podcast
    ImageUrl: string
    
    /// The category from Apple Podcasts (iTunes) under which this podcast is categorized
    AppleCategory: string
    
    /// A further refinement of the categorization of this podcast (Apple Podcasts/iTunes field / values)
    AppleSubcategory: string
    
    /// The explictness rating (iTunes field)
    Explicit: string
    
    /// The default media type for files in this podcast
    DefaultMediaType: string
    
    /// The base URL for relative URL media files for this podcast (optional; defaults to web log base)
    MediaBaseUrl: string
    
    /// The URL for funding information for the podcast
    FundingUrl: string
    
    /// The text for the funding link
    FundingText: string
    
    /// A unique identifier to follow this podcast
    PodcastGuid: string
    
    /// The medium for the content of this podcast
    Medium: string
} with
    
    /// An empty custom feed model
    static member Empty =
        { Id               = ""
          SourceType       = "category"
          SourceValue      = ""
          Path             = ""
          IsPodcast        = false
          Title            = ""
          Subtitle         = ""
          ItemsInFeed      = 25
          Summary          = ""
          DisplayedAuthor  = ""
          Email            = ""
          ImageUrl         = ""
          AppleCategory    = ""
          AppleSubcategory = ""
          Explicit         = "no"
          DefaultMediaType = "audio/mpeg"
          MediaBaseUrl     = ""
          FundingUrl       = ""
          FundingText      = ""
          PodcastGuid      = ""
          Medium           = "" }
    
    /// Create a model from a custom feed
    static member FromFeed (feed: CustomFeed) =
        let rss =
            { EditCustomFeedModel.Empty with
                Id          = string feed.Id
                SourceType  = match feed.Source with Category _ -> "category" | Tag _ -> "tag"
                SourceValue = match feed.Source with Category (CategoryId catId) -> catId | Tag tag -> tag
                Path        = string feed.Path }
        match feed.Podcast with
        | Some p ->
            { rss with
                IsPodcast        = true
                Title            = p.Title
                Subtitle         = defaultArg p.Subtitle ""
                ItemsInFeed      = p.ItemsInFeed
                Summary          = p.Summary
                DisplayedAuthor  = p.DisplayedAuthor
                Email            = p.Email
                ImageUrl         = string p.ImageUrl
                AppleCategory    = p.AppleCategory
                AppleSubcategory = defaultArg p.AppleSubcategory ""
                Explicit         = string p.Explicit
                DefaultMediaType = defaultArg p.DefaultMediaType ""
                MediaBaseUrl     = defaultArg p.MediaBaseUrl ""
                FundingUrl       = defaultArg p.FundingUrl ""
                FundingText      = defaultArg p.FundingText ""
                PodcastGuid      = p.PodcastGuid |> Option.map _.ToString().ToLowerInvariant() |> Option.defaultValue ""
                Medium           = p.Medium |> Option.map string |> Option.defaultValue "" }
        | None -> rss
    
    /// Update a feed with values from this model
    member this.UpdateFeed (feed: CustomFeed) =
        { feed with
            Source  = if this.SourceType = "tag" then Tag this.SourceValue else Category (CategoryId this.SourceValue)
            Path    = Permalink this.Path
            Podcast =
                if this.IsPodcast then
                    Some {
                        Title            = this.Title
                        Subtitle         = noneIfBlank this.Subtitle
                        ItemsInFeed      = this.ItemsInFeed
                        Summary          = this.Summary
                        DisplayedAuthor  = this.DisplayedAuthor
                        Email            = this.Email
                        ImageUrl         = Permalink this.ImageUrl
                        AppleCategory    = this.AppleCategory
                        AppleSubcategory = noneIfBlank this.AppleSubcategory
                        Explicit         = ExplicitRating.Parse this.Explicit
                        DefaultMediaType = noneIfBlank this.DefaultMediaType
                        MediaBaseUrl     = noneIfBlank this.MediaBaseUrl
                        PodcastGuid      = noneIfBlank this.PodcastGuid |> Option.map Guid.Parse
                        FundingUrl       = noneIfBlank this.FundingUrl
                        FundingText      = noneIfBlank this.FundingText
                        Medium           = noneIfBlank this.Medium |> Option.map PodcastMedium.Parse }
                else
                    None }


/// View model for a user to edit their own information
[<CLIMutable; NoComparison; NoEquality>]
type EditMyInfoModel = {
    /// The user's first name
    FirstName: string
    
    /// The user's last name
    LastName: string
    
    /// The user's preferred name
    PreferredName: string
    
    /// A new password for the user
    NewPassword: string
    
    /// A new password for the user, confirmed
    NewPasswordConfirm: string
} with
    
    /// Create an edit model from a user
    static member FromUser (user: WebLogUser) =
        { FirstName          = user.FirstName
          LastName           = user.LastName
          PreferredName      = user.PreferredName
          NewPassword        = ""
          NewPasswordConfirm = "" }


/// View model to edit a page
[<CLIMutable; NoComparison; NoEquality>]
type EditPageModel = {
    /// The ID of the page being edited
    PageId: string

    /// The title of the page
    Title: string

    /// The permalink for the page
    Permalink: string

    /// The template to use to display the page
    Template: string
    
    /// Whether this page is shown in the page list
    IsShownInPageList: bool

    /// The source format for the text
    Source: string

    /// The text of the page
    Text: string
    
    /// Names of metadata items
    MetaNames: string array
    
    /// Values of metadata items
    MetaValues: string array
} with
    
    /// Create an edit model from an existing page
    static member FromPage (page: Page) =
        let latest =
            match page.Revisions |> List.sortByDescending _.AsOf |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.Empty
        let page = if page.Metadata |> List.isEmpty then { page with Metadata = [ MetaItem.Empty ] } else page
        { PageId            = string page.Id
          Title             = page.Title
          Permalink         = string page.Permalink
          Template          = defaultArg page.Template ""
          IsShownInPageList = page.IsInPageList
          Source            = latest.Text.SourceType
          Text              = latest.Text.Text
          MetaNames         = page.Metadata |> List.map _.Name  |> Array.ofList
          MetaValues        = page.Metadata |> List.map _.Value |> Array.ofList }
    
    /// Whether this is a new page
    member this.IsNew =
        this.PageId = "new"
    
    /// Update a page with values from this model
    member this.UpdatePage (page: Page) now =
        let revision = { AsOf = now; Text = MarkupText.Parse $"{this.Source}: {this.Text}" }
        // Detect a permalink change, and add the prior one to the prior list
        match string page.Permalink with
        | "" -> page
        | link when link = this.Permalink -> page
        | _ -> { page with PriorPermalinks = page.Permalink :: page.PriorPermalinks }
        |> function
        | page ->
            { page with
                Title        = this.Title
                Permalink    = Permalink this.Permalink
                UpdatedOn    = now
                IsInPageList = this.IsShownInPageList
                Template     = match this.Template with "" -> None | tmpl -> Some tmpl
                Text         = revision.Text.AsHtml()
                Metadata     = Seq.zip this.MetaNames this.MetaValues
                               |> Seq.filter (fun it -> fst it > "")
                               |> Seq.map (fun it -> { Name = fst it; Value = snd it })
                               |> Seq.sortBy (fun it -> $"{it.Name.ToLower()} {it.Value.ToLower()}")
                               |> List.ofSeq
                Revisions    = match page.Revisions |> List.tryHead with
                               | Some r when r.Text = revision.Text -> page.Revisions
                               | _ -> revision :: page.Revisions }


/// View model to edit a post
[<CLIMutable; NoComparison; NoEquality>]
type EditPostModel = {
    /// The ID of the post being edited
    PostId: string

    /// The title of the post
    Title: string

    /// The permalink for the post
    Permalink: string

    /// The source format for the text
    Source: string

    /// The text of the post
    Text: string
    
    /// The tags for the post
    Tags: string
    
    /// The template used to display the post
    Template: string
    
    /// The category IDs for the post
    CategoryIds: string array
    
    /// The post status
    Status: string
    
    /// Whether this post should be published
    DoPublish: bool
    
    /// Names of metadata items
    MetaNames: string array
    
    /// Values of metadata items
    MetaValues: string array
    
    /// Whether to override the published date/time
    SetPublished: bool
    
    /// The published date/time to override
    PubOverride: Nullable<DateTime>
    
    /// Whether all revisions should be purged and the override date set as the updated date as well
    SetUpdated: bool
    
    /// Whether this post has a podcast episode
    IsEpisode: bool
    
    /// The URL for the media for this episode (may be permalink)
    Media: string
    
    /// The size (in bytes) of the media for this episode
    Length: int64
    
    /// The duration of the media for this episode
    Duration: string
    
    /// The media type (optional, defaults to podcast-defined media type)
    MediaType: string
    
    /// The URL for the image for this episode (may be permalink; optional, defaults to podcast image)
    ImageUrl: string
    
    /// A subtitle for the episode (optional)
    Subtitle: string
    
    /// The explicit rating for this episode (optional, defaults to podcast setting)
    Explicit: string
    
    /// The chapter source ("internal" for chapters defined here, "external" for a file link, "none" if none defined)
    ChapterSource: string
    
    /// The URL for the chapter file for the episode (may be permalink; optional)
    ChapterFile: string
    
    /// The type of the chapter file (optional; defaults to application/json+chapters if chapterFile is provided)
    ChapterType: string
    
    /// Whether the chapter file (or chapters) contains/contain waypoints
    ContainsWaypoints: bool
    
    /// The URL for the transcript (may be permalink; optional)
    TranscriptUrl: string
    
    /// The MIME type for the transcript (optional, recommended if transcriptUrl is provided)
    TranscriptType: string
    
    /// The language of the transcript (optional)
    TranscriptLang: string
    
    /// Whether the provided transcript should be presented as captions
    TranscriptCaptions: bool
    
    /// The season number (optional)
    SeasonNumber: int
    
    /// A description of this season (optional, ignored if season number is not provided)
    SeasonDescription: string
    
    /// The episode number (decimal; optional)
    EpisodeNumber: string
    
    /// A description of this episode (optional, ignored if episode number is not provided)
    EpisodeDescription: string
} with
    
    /// Create an edit model from an existing past
    static member FromPost (webLog: WebLog) (post: Post) =
        let latest =
            match post.Revisions |> List.sortByDescending _.AsOf |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.Empty
        let post    = if post.Metadata |> List.isEmpty then { post with Metadata = [ MetaItem.Empty ] } else post
        let episode = defaultArg post.Episode Episode.Empty
        { PostId             = string post.Id
          Title              = post.Title
          Permalink          = string post.Permalink
          Source             = latest.Text.SourceType
          Text               = latest.Text.Text
          Tags               = String.Join(", ", post.Tags)
          Template           = defaultArg post.Template ""
          CategoryIds        = post.CategoryIds |> List.map string |> Array.ofList
          Status             = string post.Status
          DoPublish          = false
          MetaNames          = post.Metadata |> List.map _.Name  |> Array.ofList
          MetaValues         = post.Metadata |> List.map _.Value |> Array.ofList
          SetPublished       = false
          PubOverride        = post.PublishedOn |> Option.map webLog.LocalTime |> Option.toNullable
          SetUpdated         = false
          IsEpisode          = Option.isSome post.Episode
          Media              = episode.Media
          Length             = episode.Length
          Duration           = defaultArg (episode.FormatDuration()) ""
          MediaType          = defaultArg episode.MediaType ""
          ImageUrl           = defaultArg episode.ImageUrl ""
          Subtitle           = defaultArg episode.Subtitle ""
          Explicit           = defaultArg (episode.Explicit |> Option.map string) ""
          ChapterSource      = if   Option.isSome episode.Chapters    then "internal"
                               elif Option.isSome episode.ChapterFile then "external"
                               else "none"
          ChapterFile        = defaultArg episode.ChapterFile ""
          ChapterType        = defaultArg episode.ChapterType ""
          ContainsWaypoints  = defaultArg episode.ChapterWaypoints false
          TranscriptUrl      = defaultArg episode.TranscriptUrl ""
          TranscriptType     = defaultArg episode.TranscriptType ""
          TranscriptLang     = defaultArg episode.TranscriptLang ""
          TranscriptCaptions = defaultArg episode.TranscriptCaptions false
          SeasonNumber       = defaultArg episode.SeasonNumber 0
          SeasonDescription  = defaultArg episode.SeasonDescription ""
          EpisodeNumber      = defaultArg (episode.EpisodeNumber |> Option.map string) ""  
          EpisodeDescription = defaultArg episode.EpisodeDescription "" }
    
    /// Whether this is a new post
    member this.IsNew =
        this.PostId = "new"
    
    /// Update a post with values from the submitted form
    member this.UpdatePost (post: Post) now =
        let revision  = { AsOf = now; Text = MarkupText.Parse $"{this.Source}: {this.Text}" }
        // Detect a permalink change, and add the prior one to the prior list
        match string post.Permalink with
        | "" -> post
        | link when link = this.Permalink -> post
        | _ -> { post with PriorPermalinks = post.Permalink :: post.PriorPermalinks }
        |> function
        | post ->
            { post with
                Title       = this.Title
                Permalink   = Permalink this.Permalink
                PublishedOn = if this.DoPublish then Some now else post.PublishedOn
                UpdatedOn   = now
                Text        = revision.Text.AsHtml()
                Tags        = this.Tags.Split ","
                              |> Seq.ofArray
                              |> Seq.map _.Trim().ToLower()
                              |> Seq.filter (fun it -> it <> "")
                              |> Seq.sort
                              |> List.ofSeq
                Template    = match this.Template.Trim() with "" -> None | tmpl -> Some tmpl
                CategoryIds = this.CategoryIds |> Array.map CategoryId |> List.ofArray
                Status      = if this.DoPublish then Published else post.Status
                Metadata    = Seq.zip this.MetaNames this.MetaValues
                              |> Seq.filter (fun it -> fst it > "")
                              |> Seq.map (fun it -> { Name = fst it; Value = snd it })
                              |> Seq.sortBy (fun it -> $"{it.Name.ToLower()} {it.Value.ToLower()}")
                              |> List.ofSeq
                Revisions   = match post.Revisions |> List.tryHead with
                              | Some r when r.Text = revision.Text -> post.Revisions
                              | _ -> revision :: post.Revisions
                Episode     =
                    if this.IsEpisode then
                        Some {
                            Media              = this.Media
                            Length             = this.Length
                            Duration           = noneIfBlank this.Duration
                                                 |> Option.map (TimeSpan.Parse >> Duration.FromTimeSpan)
                            MediaType          = noneIfBlank this.MediaType
                            ImageUrl           = noneIfBlank this.ImageUrl
                            Subtitle           = noneIfBlank this.Subtitle
                            Explicit           = noneIfBlank this.Explicit |> Option.map ExplicitRating.Parse
                            Chapters           = if this.ChapterSource = "internal" then
                                                     match post.Episode with
                                                     | Some e when Option.isSome e.Chapters -> e.Chapters
                                                     | Some _
                                                     | None -> Some []
                                                 else None
                            ChapterFile        = if this.ChapterSource = "external" then noneIfBlank this.ChapterFile
                                                 else None
                            ChapterType        = if this.ChapterSource = "external" then noneIfBlank this.ChapterType
                                                 else None
                            ChapterWaypoints   = if this.ChapterSource = "none" then None
                                                 elif this.ContainsWaypoints then Some true
                                                 else None
                            TranscriptUrl      = noneIfBlank this.TranscriptUrl
                            TranscriptType     = noneIfBlank this.TranscriptType
                            TranscriptLang     = noneIfBlank this.TranscriptLang
                            TranscriptCaptions = if this.TranscriptCaptions then Some true else None
                            SeasonNumber       = if this.SeasonNumber = 0 then None else Some this.SeasonNumber
                            SeasonDescription  = noneIfBlank this.SeasonDescription
                            EpisodeNumber      = match noneIfBlank this.EpisodeNumber |> Option.map Double.Parse with
                                                 | Some it when it = 0.0 -> None
                                                 | Some it -> Some (double it)
                                                 | None -> None
                            EpisodeDescription = noneIfBlank this.EpisodeDescription
                        }
                    else
                        None }


/// View model to add/edit a redirect rule
[<CLIMutable; NoComparison; NoEquality>]
type EditRedirectRuleModel = {
    /// The ID (index) of the rule being edited
    RuleId: int

    /// The "from" side of the rule
    From: string

    /// The "to" side of the rule
    To: string

    /// Whether this rule uses a regular expression
    IsRegex: bool

    /// Whether a new rule should be inserted at the top or appended to the end (ignored for edits)
    InsertAtTop: bool
} with

    /// Create a model from an existing rule
    static member FromRule idx (rule: RedirectRule) =
        { RuleId      = idx
          From        = rule.From
          To          = rule.To
          IsRegex     = rule.IsRegex
          InsertAtTop = false }
    
    /// Update a rule with the values from this model
    member this.ToRule() =
        { From    = this.From
          To      = this.To
          IsRegex = this.IsRegex }


/// View model to edit RSS settings
[<CLIMutable; NoComparison; NoEquality>]
type EditRssModel = {
    /// Whether the site feed of posts is enabled
    IsFeedEnabled: bool
    
    /// The name of the file generated for the site feed
    FeedName: string
    
    /// Override the "posts per page" setting for the site feed
    ItemsInFeed: int
    
    /// Whether feeds are enabled for all categories
    IsCategoryEnabled: bool
    
    /// Whether feeds are enabled for all tags
    IsTagEnabled: bool
    
    /// A copyright string to be placed in all feeds
    Copyright: string
} with
    
    /// Create an edit model from a set of RSS options
    static member FromRssOptions (rss: RssOptions) =
        { IsFeedEnabled     = rss.IsFeedEnabled
          FeedName          = rss.FeedName
          ItemsInFeed       = defaultArg rss.ItemsInFeed 0
          IsCategoryEnabled = rss.IsCategoryEnabled
          IsTagEnabled      = rss.IsTagEnabled
          Copyright         = defaultArg rss.Copyright "" }
    
    /// Update RSS options from values in this model
    member this.UpdateOptions (rss: RssOptions) =
        { rss with
            IsFeedEnabled     = this.IsFeedEnabled
            FeedName          = this.FeedName
            ItemsInFeed       = if this.ItemsInFeed = 0 then None else Some this.ItemsInFeed
            IsCategoryEnabled = this.IsCategoryEnabled
            IsTagEnabled      = this.IsTagEnabled
            Copyright         = noneIfBlank this.Copyright }


/// View model to edit a tag mapping
[<CLIMutable; NoComparison; NoEquality>]
type EditTagMapModel = {
    /// The ID of the tag mapping being edited
    Id: string
    
    /// The tag being mapped to a different link value
    Tag: string
    
    /// The link value for the tag
    UrlValue: string
} with
    
    /// Create an edit model from the tag mapping
    static member FromMapping (tagMap: TagMap) : EditTagMapModel =
        { Id       = string tagMap.Id
          Tag      = tagMap.Tag
          UrlValue = tagMap.UrlValue }
    
    /// Whether this is a new tag mapping
    member this.IsNew =
        this.Id = "new"


/// View model to display a user's information
[<CLIMutable; NoComparison; NoEquality>]
type EditUserModel = {
    /// The ID of the user
    Id: string

    /// The user's access level
    AccessLevel: string
    
    /// The user name (e-mail address)
    Email: string

    /// The URL of the user's personal site
    Url: string

    /// The user's first name
    FirstName: string

    /// The user's last name
    LastName: string

    /// The user's preferred name
    PreferredName: string
    
    /// The user's password
    Password: string
    
    /// Confirmation of the user's password
    PasswordConfirm: string
} with
    
    /// Construct a user edit form from a web log user
    static member FromUser (user: WebLogUser) =
        { Id              = string user.Id
          AccessLevel     = string user.AccessLevel
          Url             = defaultArg user.Url ""
          Email           = user.Email
          FirstName       = user.FirstName
          LastName        = user.LastName
          PreferredName   = user.PreferredName
          Password        = ""
          PasswordConfirm = "" }
    
    /// Is this a new user?
    member this.IsNew =
        this.Id = "new"
    
    /// Update a user with values from this model (excludes password)
    member this.UpdateUser (user: WebLogUser) =
        { user with
            AccessLevel   = AccessLevel.Parse this.AccessLevel
            Email         = this.Email
            Url           = noneIfBlank this.Url
            FirstName     = this.FirstName
            LastName      = this.LastName
            PreferredName = this.PreferredName }


/// The model to use to allow a user to log on
[<CLIMutable; NoComparison; NoEquality>]
type LogOnModel = {
    /// The user's e-mail address
    EmailAddress : string

    /// The user's password
    Password : string
    
    /// Where the user should be redirected once they have logged on
    ReturnTo : string option
} with
    
    /// An empty log on model
    static member Empty =
        { EmailAddress = ""; Password = ""; ReturnTo = None }


/// View model to manage chapters
[<CLIMutable; NoComparison; NoEquality>]
type ManageChaptersModel = {
    /// The post ID for the chapters being edited
    Id: string
    
    /// The title of the post for which chapters are being edited
    Title: string
    
    /// The chapters for the post
    Chapters: Chapter list
} with
    
    /// Create a model from a post and its episode's chapters
    static member Create (post: Post) =
        { Id       = string post.Id
          Title    = post.Title
          Chapters = post.Episode.Value.Chapters.Value }
    

/// View model to manage permalinks
[<CLIMutable; NoComparison; NoEquality>]
type ManagePermalinksModel = {
    /// The ID for the entity being edited
    Id: string
    
    /// The type of entity being edited ("page" or "post")
    Entity: string
    
    /// The current title of the page or post
    CurrentTitle: string
    
    /// The current permalink of the page or post
    CurrentPermalink: string
    
    /// The prior permalinks for the page or post
    Prior: string array
} with
    
    /// Create a permalink model from a page
    static member FromPage (page: Page) =
        { Id               = string page.Id
          Entity           = "page"
          CurrentTitle     = page.Title
          CurrentPermalink = string page.Permalink
          Prior            = page.PriorPermalinks |> List.map string |> Array.ofList }

    /// Create a permalink model from a post
    static member FromPost (post: Post) =
        { Id               = string post.Id
          Entity           = "post"
          CurrentTitle     = post.Title
          CurrentPermalink = string post.Permalink
          Prior            = post.PriorPermalinks |> List.map string |> Array.ofList }


/// View model to manage revisions
[<NoComparison; NoEquality>]
type ManageRevisionsModel = {
    /// The ID for the entity being edited
    Id: string
    
    /// The type of entity being edited ("page" or "post")
    Entity: string
    
    /// The current title of the page or post
    CurrentTitle: string
    
    /// The revisions for the page or post
    Revisions: Revision list
} with
    
    /// Create a revision model from a page
    static member FromPage (page: Page) =
        { Id           = string page.Id
          Entity       = "page"
          CurrentTitle = page.Title
          Revisions    = page.Revisions }

    /// Create a revision model from a post
    static member FromPost (post: Post) =
        { Id           = string post.Id
          Entity       = "post"
          CurrentTitle = post.Title
          Revisions    = post.Revisions }


/// View model for posts in a list
[<NoComparison; NoEquality>]
type PostListItem = {
    /// The ID of the post
    Id: string
    
    /// The ID of the user who authored the post
    AuthorId: string
    
    /// The status of the post
    Status: string
    
    /// The title of the post
    Title: string
    
    /// The permalink for the post
    Permalink: string
    
    /// When this post was published
    PublishedOn: Nullable<DateTime>
    
    /// When this post was last updated
    UpdatedOn: DateTime
    
    /// The text of the post
    Text: string
    
    /// The IDs of the categories for this post
    CategoryIds: string list
    
    /// Tags for the post
    Tags: string list
    
    /// The podcast episode information for this post
    Episode: Episode option
    
    /// Metadata for the post
    Metadata: MetaItem list
} with

    /// Create a post list item from a post
    static member FromPost (webLog: WebLog) (post: Post) =
        { Id          = string post.Id
          AuthorId    = string post.AuthorId
          Status      = string post.Status
          Title       = post.Title
          Permalink   = string post.Permalink
          PublishedOn = post.PublishedOn |> Option.map webLog.LocalTime |> Option.toNullable
          UpdatedOn   = webLog.LocalTime post.UpdatedOn
          Text        = addBaseToRelativeUrls webLog.ExtraPath post.Text
          CategoryIds = post.CategoryIds |> List.map string
          Tags        = post.Tags
          Episode     = post.Episode
          Metadata    = post.Metadata }


/// View model for displaying posts
type PostDisplay = {
    /// The posts to be displayed
    Posts: PostListItem array
    
    /// Author ID -> name lookup
    Authors: MetaItem list
    
    /// A subtitle for the page
    Subtitle: string option
    
    /// The link to view newer (more recent) posts
    NewerLink: string option
    
    /// The name of the next newer post (single-post only)
    NewerName: string option
    
    /// The link to view older (less recent) posts
    OlderLink: string option
    
    /// The name of the next older post (single-post only)
    OlderName: string option
}


/// View model for editing web log settings
[<CLIMutable; NoComparison; NoEquality>]
type SettingsModel = {
    /// The name of the web log
    Name: string

    /// The slug of the web log
    Slug: string
    
    /// The subtitle of the web log
    Subtitle: string

    /// The default page
    DefaultPage: string

    /// How many posts should appear on index pages
    PostsPerPage: int

    /// The time zone in which dates/times should be displayed
    TimeZone: string
    
    /// The theme to use to display the web log
    ThemeId: string
    
    /// Whether to automatically load htmx
    AutoHtmx: bool
    
    /// The default location for uploads
    Uploads: string
} with
    
    /// Create a settings model from a web log
    static member FromWebLog(webLog: WebLog) =
        { Name         = webLog.Name
          Slug         = webLog.Slug
          Subtitle     = defaultArg webLog.Subtitle ""
          DefaultPage  = webLog.DefaultPage
          PostsPerPage = webLog.PostsPerPage
          TimeZone     = webLog.TimeZone
          ThemeId      = string webLog.ThemeId
          AutoHtmx     = webLog.AutoHtmx
          Uploads      = string webLog.Uploads }
    
    /// Update a web log with settings from the form
    member this.Update(webLog: WebLog) =
        { webLog with
            Name         = this.Name
            Slug         = this.Slug
            Subtitle     = if this.Subtitle = "" then None else Some this.Subtitle
            DefaultPage  = this.DefaultPage
            PostsPerPage = this.PostsPerPage
            TimeZone     = this.TimeZone
            ThemeId      = ThemeId this.ThemeId
            AutoHtmx     = this.AutoHtmx
            Uploads      = UploadDestination.Parse this.Uploads }


/// View model for uploading a file
[<CLIMutable; NoComparison; NoEquality>]
type UploadFileModel = {
    /// The upload destination
    Destination : string
}


/// View model for uploading a theme
[<CLIMutable; NoComparison; NoEquality>]
type UploadThemeModel = {
    /// Whether the uploaded theme should overwrite an existing theme
    DoOverwrite : bool
}


/// A message displayed to the user
[<CLIMutable; NoComparison; NoEquality>]
type UserMessage = {
    /// The level of the message
    Level: string
    
    /// The message
    Message: string
    
    /// Further details about the message
    Detail: string option
} with
    
    /// An empty user message (use one of the others for pre-filled level)
    static member Empty = { Level = ""; Message = ""; Detail = None }
    
    /// A blank success message
    static member Success = { UserMessage.Empty with Level = "success" }
    
    /// A blank informational message
    static member Info = { UserMessage.Empty with Level = "primary" }
    
    /// A blank warning message
    static member Warning = { UserMessage.Empty with Level = "warning" }
    
    /// A blank error message
    static member Error = { UserMessage.Empty with Level = "danger" }
