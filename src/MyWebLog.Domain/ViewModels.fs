namespace MyWebLog.ViewModels

open System
open MyWebLog

/// Helper functions for view models
[<AutoOpen>]
module private Helpers =
    
    /// Create a string option if a string is blank
    let noneIfBlank (it : string) =
        match (defaultArg (Option.ofObj it) "").Trim () with "" -> None | trimmed -> Some trimmed


/// The model used to display the admin dashboard
[<NoComparison; NoEquality>]
type DashboardModel =
    {   /// The number of published posts
        Posts : int

        /// The number of post drafts
        Drafts : int

        /// The number of pages
        Pages : int

        /// The number of pages in the page list
        ListedPages : int

        /// The number of categories
        Categories : int

        /// The top-level categories
        TopLevelCategories : int
    }


/// Details about a category, used to display category lists
[<NoComparison; NoEquality>]
type DisplayCategory =
    {   /// The ID of the category
        Id : string
        
        /// The slug for the category
        Slug : string
        
        /// The name of the category
        Name : string
        
        /// A description of the category
        Description : string option
        
        /// The parent category names for this (sub)category
        ParentNames : string[]
        
        /// The number of posts in this category
        PostCount : int
    }


/// A display version of a custom feed definition
type DisplayCustomFeed =
    {   /// The ID of the custom feed
        Id : string
        
        /// The source of the custom feed
        Source : string
        
        /// The relative path at which the custom feed is served
        Path : string
        
        /// Whether this custom feed is for a podcast
        IsPodcast : bool
    }
    
    /// Create a display version from a custom feed
    static member fromFeed (cats : DisplayCategory[]) (feed : CustomFeed) : DisplayCustomFeed =
        let source =
            match feed.source with
            | Category (CategoryId catId) -> $"Category: {(cats |> Array.find (fun cat -> cat.Id = catId)).Name}"
            | Tag tag -> $"Tag: {tag}"
        { Id        = CustomFeedId.toString feed.id
          Source    = source
          Path      = Permalink.toString feed.path
          IsPodcast = Option.isSome feed.podcast
        }


/// Details about a page used to display page lists
[<NoComparison; NoEquality>]
type DisplayPage =
    {   /// The ID of this page
        Id : string

        /// The ID of the author of this page
        AuthorId : string
        
        /// The title of the page
        Title : string

        /// The link at which this page is displayed
        Permalink : string

        /// When this page was published
        PublishedOn : DateTime

        /// When this page was last updated
        UpdatedOn : DateTime

        /// Whether this page shows as part of the web log's navigation
        ShowInPageList : bool
        
        /// Is this the default page?
        IsDefault : bool
        
        /// The text of the page
        Text : string
        
        /// The metadata for the page
        Metadata : MetaItem list
    }
    
    /// Create a minimal display page (no text or metadata) from a database page
    static member fromPageMinimal webLog (page : Page) =
        let pageId = PageId.toString page.id
        { Id             = pageId
          AuthorId       = WebLogUserId.toString page.authorId
          Title          = page.title
          Permalink      = Permalink.toString page.permalink
          PublishedOn    = page.publishedOn
          UpdatedOn      = page.updatedOn
          ShowInPageList = page.showInPageList
          IsDefault      = pageId = webLog.defaultPage
          Text           = ""
          Metadata       = []
        }
    
    /// Create a display page from a database page
    static member fromPage webLog (page : Page) =
        let _, extra = WebLog.hostAndPath webLog
        let pageId = PageId.toString page.id
        { Id             = pageId
          AuthorId       = WebLogUserId.toString page.authorId
          Title          = page.title
          Permalink      = Permalink.toString page.permalink
          PublishedOn    = page.publishedOn
          UpdatedOn      = page.updatedOn
          ShowInPageList = page.showInPageList
          IsDefault      = pageId = webLog.defaultPage
          Text           = if extra = "" then page.text else page.text.Replace ("href=\"/", $"href=\"{extra}/")
          Metadata       = page.metadata
        }


/// Information about a revision used for display
[<NoComparison; NoEquality>]
type DisplayRevision =
    {   /// The as-of date/time for the revision
        AsOf : DateTime
        
        /// The as-of date/time for the revision in the web log's local time zone
        AsOfLocal : DateTime
        
        /// The format of the text of the revision
        Format : string
    }
with

    /// Create a display revision from an actual revision
    static member fromRevision webLog (rev : Revision) =
        { AsOf      = rev.asOf
          AsOfLocal = WebLog.localTime webLog rev.asOf
          Format    = MarkupText.sourceType rev.text
        }


open System.IO

/// Information about an uploaded file used for display
[<NoComparison; NoEquality>]
type DisplayUpload =
    {   /// The ID of the uploaded file
        Id : string
        
        /// The name of the uploaded file
        Name : string
        
        /// The path at which the file is served
        Path : string
        
        /// The date/time the file was updated
        UpdatedOn : DateTime option
        
        /// The source for this file (created from UploadDestination DU)
        Source : string
    }
    
    /// Create a display uploaded file
    static member fromUpload webLog source (upload : Upload) =
        let path = Permalink.toString upload.path
        let name = Path.GetFileName path
        { Id        = UploadId.toString upload.id
          Name      = name
          Path      = path.Replace (name, "")
          UpdatedOn = Some (WebLog.localTime webLog upload.updatedOn)
          Source    = UploadDestination.toString source
        }


/// View model for editing categories
[<CLIMutable; NoComparison; NoEquality>]
type EditCategoryModel =
    {   /// The ID of the category being edited
        CategoryId : string
        
        /// The name of the category
        Name : string
        
        /// The category's URL slug
        Slug : string
        
        /// A description of the category (optional)
        Description : string
        
        /// The ID of the category for which this is a subcategory (optional)
        ParentId : string
    }
    
    /// Create an edit model from an existing category 
    static member fromCategory (cat : Category) =
        { CategoryId  = CategoryId.toString cat.id
          Name        = cat.name
          Slug        = cat.slug
          Description = defaultArg cat.description ""
          ParentId    = cat.parentId |> Option.map CategoryId.toString |> Option.defaultValue ""
        }


/// View model to edit a custom RSS feed
[<CLIMutable; NoComparison; NoEquality>]
type EditCustomFeedModel =
    {   /// The ID of the feed being editing
        Id : string
        
        /// The type of source for this feed ("category" or "tag")
        SourceType : string
        
        /// The category ID or tag on which this feed is based
        SourceValue : string
        
        /// The relative path at which this feed is served
        Path : string
        
        /// Whether this feed defines a podcast
        IsPodcast : bool
        
        /// The title of the podcast
        Title : string
        
        /// A subtitle for the podcast
        Subtitle : string
        
        /// The number of items in the podcast feed
        ItemsInFeed : int
        
        /// A summary of the podcast (iTunes field)
        Summary : string
        
        /// The display name of the podcast author (iTunes field)
        DisplayedAuthor : string
        
        /// The e-mail address of the user who registered the podcast at iTunes
        Email : string
        
        /// The link to the image for the podcast
        ImageUrl : string
        
        /// The category from iTunes under which this podcast is categorized
        iTunesCategory : string
        
        /// A further refinement of the categorization of this podcast (iTunes field / values)
        iTunesSubcategory : string
        
        /// The explictness rating (iTunes field)
        Explicit : string
        
        /// The default media type for files in this podcast
        DefaultMediaType : string
        
        /// The base URL for relative URL media files for this podcast (optional; defaults to web log base)
        MediaBaseUrl : string
        
        /// The URL for funding information for the podcast
        FundingUrl : string
        
        /// The text for the funding link
        FundingText : string
        
        /// A unique identifier to follow this podcast
        PodcastGuid : string
        
        /// The medium for the content of this podcast
        Medium : string
    }
    
    /// An empty custom feed model
    static member empty =
        { Id                = ""
          SourceType        = "category"
          SourceValue       = ""
          Path              = ""
          IsPodcast         = false
          Title             = ""
          Subtitle          = ""
          ItemsInFeed       = 25
          Summary           = ""
          DisplayedAuthor   = ""
          Email             = ""
          ImageUrl          = ""
          iTunesCategory    = ""
          iTunesSubcategory = ""
          Explicit          = "no"
          DefaultMediaType  = "audio/mpeg"
          MediaBaseUrl      = ""
          FundingUrl        = ""
          FundingText       = ""
          PodcastGuid       = ""
          Medium            = ""
        }
    
    /// Create a model from a custom feed
    static member fromFeed (feed : CustomFeed) =
        let rss =
            { EditCustomFeedModel.empty with
                Id          = CustomFeedId.toString feed.id
                SourceType  = match feed.source with Category _ -> "category" | Tag _ -> "tag"
                SourceValue = match feed.source with Category (CategoryId catId) -> catId | Tag tag -> tag
                Path        = Permalink.toString feed.path
            }
        match feed.podcast with
        | Some p ->
            { rss with
                IsPodcast         = true
                Title             = p.title
                Subtitle          = defaultArg p.subtitle ""
                ItemsInFeed       = p.itemsInFeed
                Summary           = p.summary
                DisplayedAuthor   = p.displayedAuthor
                Email             = p.email
                ImageUrl          = Permalink.toString p.imageUrl
                iTunesCategory    = p.iTunesCategory
                iTunesSubcategory = defaultArg p.iTunesSubcategory ""
                Explicit          = ExplicitRating.toString p.explicit
                DefaultMediaType  = defaultArg p.defaultMediaType ""
                MediaBaseUrl      = defaultArg p.mediaBaseUrl ""
                FundingUrl        = defaultArg p.fundingUrl ""
                FundingText       = defaultArg p.fundingText ""
                PodcastGuid       = p.guid
                                    |> Option.map (fun it -> it.ToString().ToLowerInvariant ())
                                    |> Option.defaultValue ""
                Medium            = p.medium |> Option.map PodcastMedium.toString |> Option.defaultValue ""
            }
        | None -> rss
    
    /// Update a feed with values from this model
    member this.updateFeed (feed : CustomFeed) =
        { feed with
            source  = if this.SourceType = "tag" then Tag this.SourceValue else Category (CategoryId this.SourceValue)
            path    = Permalink this.Path
            podcast =
                if this.IsPodcast then
                    Some {
                        title             = this.Title
                        subtitle          = noneIfBlank this.Subtitle
                        itemsInFeed       = this.ItemsInFeed
                        summary           = this.Summary
                        displayedAuthor   = this.DisplayedAuthor
                        email             = this.Email
                        imageUrl          = Permalink this.ImageUrl
                        iTunesCategory    = this.iTunesCategory
                        iTunesSubcategory = noneIfBlank this.iTunesSubcategory
                        explicit          = ExplicitRating.parse this.Explicit
                        defaultMediaType  = noneIfBlank this.DefaultMediaType
                        mediaBaseUrl      = noneIfBlank this.MediaBaseUrl
                        guid              = noneIfBlank this.PodcastGuid |> Option.map Guid.Parse
                        fundingUrl        = noneIfBlank this.FundingUrl
                        fundingText       = noneIfBlank this.FundingText
                        medium            = noneIfBlank this.Medium |> Option.map PodcastMedium.parse
                    }
                else
                    None
        }

/// View model to edit a page
[<CLIMutable; NoComparison; NoEquality>]
type EditPageModel =
    {   /// The ID of the page being edited
        PageId : string

        /// The title of the page
        Title : string

        /// The permalink for the page
        Permalink : string

        /// The template to use to display the page
        Template : string
        
        /// Whether this page is shown in the page list
        IsShownInPageList : bool

        /// The source format for the text
        Source : string

        /// The text of the page
        Text : string
        
        /// Names of metadata items
        MetaNames : string[]
        
        /// Values of metadata items
        MetaValues : string[]
    }
    
    /// Create an edit model from an existing page
    static member fromPage (page : Page) =
        let latest =
            match page.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let page = if page.metadata |> List.isEmpty then { page with metadata = [ MetaItem.empty ] } else page
        { PageId            = PageId.toString page.id
          Title             = page.title
          Permalink         = Permalink.toString page.permalink
          Template          = defaultArg page.template ""
          IsShownInPageList = page.showInPageList
          Source            = MarkupText.sourceType latest.text
          Text              = MarkupText.text       latest.text
          MetaNames         = page.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          MetaValues        = page.metadata |> List.map (fun m -> m.value) |> Array.ofList
        }


/// View model to edit a post
[<CLIMutable; NoComparison; NoEquality>]
type EditPostModel =
    {   /// The ID of the post being edited
        PostId : string

        /// The title of the post
        Title : string

        /// The permalink for the post
        Permalink : string

        /// The source format for the text
        Source : string

        /// The text of the post
        Text : string
        
        /// The tags for the post
        Tags : string
        
        /// The template used to display the post
        Template : string
        
        /// The category IDs for the post
        CategoryIds : string[]
        
        /// The post status
        Status : string
        
        /// Whether this post should be published
        DoPublish : bool
        
        /// Names of metadata items
        MetaNames : string[]
        
        /// Values of metadata items
        MetaValues : string[]
        
        /// Whether to override the published date/time
        SetPublished : bool
        
        /// The published date/time to override
        PubOverride : Nullable<DateTime>
        
        /// Whether all revisions should be purged and the override date set as the updated date as well
        SetUpdated : bool
        
        /// Whether this post has a podcast episode
        IsEpisode : bool
        
        /// The URL for the media for this episode (may be permalink)
        Media : string
        
        /// The size (in bytes) of the media for this episode
        Length : int64
        
        /// The duration of the media for this episode
        Duration : string
        
        /// The media type (optional, defaults to podcast-defined media type)
        MediaType : string
        
        /// The URL for the image for this episode (may be permalink; optional, defaults to podcast image)
        ImageUrl : string
        
        /// A subtitle for the episode (optional)
        Subtitle : string
        
        /// The explicit rating for this episode (optional, defaults to podcast setting)
        Explicit : string
        
        /// The URL for the chapter file for the episode (may be permalink; optional)
        ChapterFile : string
        
        /// The type of the chapter file (optional; defaults to application/json+chapters if chapterFile is provided)
        ChapterType : string
        
        /// The URL for the transcript (may be permalink; optional)
        TranscriptUrl : string
        
        /// The MIME type for the transcript (optional, recommended if transcriptUrl is provided)
        TranscriptType : string
        
        /// The language of the transcript (optional)
        TranscriptLang : string
        
        /// Whether the provided transcript should be presented as captions
        TranscriptCaptions : bool
        
        /// The season number (optional)
        SeasonNumber : int
        
        /// A description of this season (optional, ignored if season number is not provided)
        SeasonDescription : string
        
        /// The episode number (decimal; optional)
        EpisodeNumber : string
        
        /// A description of this episode (optional, ignored if episode number is not provided)
        EpisodeDescription : string
    }
    
    /// Create an edit model from an existing past
    static member fromPost webLog (post : Post) =
        let latest =
            match post.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let post = if post.metadata |> List.isEmpty then { post with metadata = [ MetaItem.empty ] } else post
        let episode = defaultArg post.episode Episode.empty
        { PostId             = PostId.toString post.id
          Title              = post.title
          Permalink          = Permalink.toString post.permalink
          Source             = MarkupText.sourceType latest.text
          Text               = MarkupText.text       latest.text
          Tags               = String.Join (", ", post.tags)
          Template           = defaultArg post.template ""
          CategoryIds        = post.categoryIds |> List.map CategoryId.toString |> Array.ofList
          Status             = PostStatus.toString post.status
          DoPublish          = false
          MetaNames          = post.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          MetaValues         = post.metadata |> List.map (fun m -> m.value) |> Array.ofList
          SetPublished       = false
          PubOverride        = post.publishedOn |> Option.map (WebLog.localTime webLog) |> Option.toNullable
          SetUpdated         = false
          IsEpisode          = Option.isSome post.episode
          Media              = episode.media
          Length             = episode.length
          Duration           = defaultArg (episode.duration |> Option.map (fun it -> it.ToString """hh\:mm\:ss""")) ""
          MediaType          = defaultArg episode.mediaType ""
          ImageUrl           = defaultArg episode.imageUrl ""
          Subtitle           = defaultArg episode.subtitle ""
          Explicit           = defaultArg (episode.explicit |> Option.map ExplicitRating.toString) ""
          ChapterFile        = defaultArg episode.chapterFile ""
          ChapterType        = defaultArg episode.chapterType ""
          TranscriptUrl      = defaultArg episode.transcriptUrl ""
          TranscriptType     = defaultArg episode.transcriptType ""
          TranscriptLang     = defaultArg episode.transcriptLang ""
          TranscriptCaptions = defaultArg episode.transcriptCaptions false
          SeasonNumber       = defaultArg episode.seasonNumber 0
          SeasonDescription  = defaultArg episode.seasonDescription ""
          EpisodeNumber      = defaultArg (episode.episodeNumber |> Option.map string) ""  
          EpisodeDescription = defaultArg episode.episodeDescription ""
        }
    
    /// Update a post with values from the submitted form
    member this.updatePost (post : Post) (revision : Revision) now =
        { post with
            title       = this.Title
            permalink   = Permalink this.Permalink
            publishedOn = if this.DoPublish then Some now else post.publishedOn
            updatedOn   = now
            text        = MarkupText.toHtml revision.text
            tags        = this.Tags.Split ","
                          |> Seq.ofArray
                          |> Seq.map (fun it -> it.Trim().ToLower ())
                          |> Seq.filter (fun it -> it <> "")
                          |> Seq.sort
                          |> List.ofSeq
            template    = match this.Template.Trim () with "" -> None | tmpl -> Some tmpl
            categoryIds = this.CategoryIds |> Array.map CategoryId |> List.ofArray
            status      = if this.DoPublish then Published else post.status
            metadata    = Seq.zip this.MetaNames this.MetaValues
                          |> Seq.filter (fun it -> fst it > "")
                          |> Seq.map (fun it -> { name = fst it; value = snd it })
                          |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                          |> List.ofSeq
            revisions   = match post.revisions |> List.tryHead with
                          | Some r when r.text = revision.text -> post.revisions
                          | _ -> revision :: post.revisions
            episode     =
                if this.IsEpisode then
                    Some {
                        media              = this.Media
                        length             = this.Length
                        duration           = noneIfBlank this.Duration |> Option.map TimeSpan.Parse
                        mediaType          = noneIfBlank this.MediaType
                        imageUrl           = noneIfBlank this.ImageUrl
                        subtitle           = noneIfBlank this.Subtitle
                        explicit           = noneIfBlank this.Explicit |> Option.map ExplicitRating.parse
                        chapterFile        = noneIfBlank this.ChapterFile
                        chapterType        = noneIfBlank this.ChapterType
                        transcriptUrl      = noneIfBlank this.TranscriptUrl
                        transcriptType     = noneIfBlank this.TranscriptType
                        transcriptLang     = noneIfBlank this.TranscriptLang
                        transcriptCaptions = if this.TranscriptCaptions then Some true else None
                        seasonNumber       = if this.SeasonNumber = 0 then None else Some this.SeasonNumber
                        seasonDescription  = noneIfBlank this.SeasonDescription
                        episodeNumber      = match noneIfBlank this.EpisodeNumber |> Option.map Double.Parse with
                                             | Some it when it = 0.0 -> None
                                             | Some it -> Some (double it)
                                             | None -> None
                        episodeDescription = noneIfBlank this.EpisodeDescription
                    }
                else
                    None
        }


/// View model to edit RSS settings
[<CLIMutable; NoComparison; NoEquality>]
type EditRssModel =
    {   /// Whether the site feed of posts is enabled
        IsFeedEnabled : bool
        
        /// The name of the file generated for the site feed
        FeedName : string
        
        /// Override the "posts per page" setting for the site feed
        ItemsInFeed : int
        
        /// Whether feeds are enabled for all categories
        IsCategoryEnabled : bool
        
        /// Whether feeds are enabled for all tags
        IsTagEnabled : bool
        
        /// A copyright string to be placed in all feeds
        Copyright : string
    }
    
    /// Create an edit model from a set of RSS options
    static member fromRssOptions (rss : RssOptions) =
        { IsFeedEnabled     = rss.feedEnabled
          FeedName          = rss.feedName
          ItemsInFeed       = defaultArg rss.itemsInFeed 0
          IsCategoryEnabled = rss.categoryEnabled
          IsTagEnabled      = rss.tagEnabled
          Copyright         = defaultArg rss.copyright ""
        }
    
    /// Update RSS options from values in this mode
    member this.updateOptions (rss : RssOptions) =
        { rss with
            feedEnabled     = this.IsFeedEnabled
            feedName        = this.FeedName
            itemsInFeed     = if this.ItemsInFeed = 0 then None else Some this.ItemsInFeed
            categoryEnabled = this.IsCategoryEnabled
            tagEnabled      = this.IsTagEnabled
            copyright       = noneIfBlank this.Copyright
        }


/// View model to edit a tag mapping
[<CLIMutable; NoComparison; NoEquality>]
type EditTagMapModel =
    {   /// The ID of the tag mapping being edited
        Id : string
        
        /// The tag being mapped to a different link value
        Tag : string
        
        /// The link value for the tag
        UrlValue : string
    }
    
    /// Whether this is a new tag mapping
    member this.IsNew = this.Id = "new"
    
    /// Create an edit model from the tag mapping
    static member fromMapping (tagMap : TagMap) : EditTagMapModel =
        { Id       = TagMapId.toString tagMap.id
          Tag      = tagMap.tag
          UrlValue = tagMap.urlValue
        }


/// View model to edit a user
[<CLIMutable; NoComparison; NoEquality>]
type EditUserModel =
    {   /// The user's first name
        FirstName : string
        
        /// The user's last name
        LastName : string
        
        /// The user's preferred name
        PreferredName : string
        
        /// A new password for the user
        NewPassword : string
        
        /// A new password for the user, confirmed
        NewPasswordConfirm : string
    }
    /// Create an edit model from a user
    static member fromUser (user : WebLogUser) =
        { FirstName          = user.firstName
          LastName           = user.lastName
          PreferredName      = user.preferredName
          NewPassword        = ""
          NewPasswordConfirm = ""
        }


/// The model to use to allow a user to log on
[<CLIMutable; NoComparison; NoEquality>]
type LogOnModel =
    {   /// The user's e-mail address
        EmailAddress : string
    
        /// The user's password
        Password : string
        
        /// Where the user should be redirected once they have logged on
        ReturnTo : string option
    }
    
    /// An empty log on model
    static member empty =
        { EmailAddress = ""; Password = ""; ReturnTo = None }


/// View model to manage permalinks
[<CLIMutable; NoComparison; NoEquality>]
type ManagePermalinksModel =
    {   /// The ID for the entity being edited
        Id : string
        
        /// The type of entity being edited ("page" or "post")
        Entity : string
        
        /// The current title of the page or post
        CurrentTitle : string
        
        /// The current permalink of the page or post
        CurrentPermalink : string
        
        /// The prior permalinks for the page or post
        Prior : string[]
    }
    
    /// Create a permalink model from a page
    static member fromPage (pg : Page) =
        { Id               = PageId.toString pg.id
          Entity           = "page"
          CurrentTitle     = pg.title
          CurrentPermalink = Permalink.toString pg.permalink
          Prior            = pg.priorPermalinks |> List.map Permalink.toString |> Array.ofList
        }

    /// Create a permalink model from a post
    static member fromPost (post : Post) =
        { Id               = PostId.toString post.id
          Entity           = "post"
          CurrentTitle     = post.title
          CurrentPermalink = Permalink.toString post.permalink
          Prior            = post.priorPermalinks |> List.map Permalink.toString |> Array.ofList
        }


/// View model to manage revisions
[<NoComparison; NoEquality>]
type ManageRevisionsModel =
    {   /// The ID for the entity being edited
        Id : string
        
        /// The type of entity being edited ("page" or "post")
        Entity : string
        
        /// The current title of the page or post
        CurrentTitle : string
        
        /// The revisions for the page or post
        Revisions : DisplayRevision[]
    }
    
    /// Create a revision model from a page
    static member fromPage webLog (pg : Page) =
        { Id           = PageId.toString pg.id
          Entity       = "page"
          CurrentTitle = pg.title
          Revisions    = pg.revisions |> List.map (DisplayRevision.fromRevision webLog) |> Array.ofList
        }

    /// Create a revision model from a post
    static member fromPost webLog (post : Post) =
        { Id           = PostId.toString post.id
          Entity       = "post"
          CurrentTitle = post.title
          Revisions    = post.revisions |> List.map (DisplayRevision.fromRevision webLog) |> Array.ofList
        }


/// View model for posts in a list
[<NoComparison; NoEquality>]
type PostListItem =
    {   /// The ID of the post
        Id : string
        
        /// The ID of the user who authored the post
        AuthorId : string
        
        /// The status of the post
        Status : string
        
        /// The title of the post
        Title : string
        
        /// The permalink for the post
        Permalink : string
        
        /// When this post was published
        PublishedOn : Nullable<DateTime>
        
        /// When this post was last updated
        UpdatedOn : DateTime
        
        /// The text of the post
        Text : string
        
        /// The IDs of the categories for this post
        CategoryIds : string list
        
        /// Tags for the post
        Tags : string list
        
        /// The podcast episode information for this post
        Episode : Episode option
        
        /// Metadata for the post
        Metadata : MetaItem list
    }

    /// Create a post list item from a post
    static member fromPost (webLog : WebLog) (post : Post) =
        let _, extra = WebLog.hostAndPath webLog
        let inTZ     = WebLog.localTime   webLog
        { Id          = PostId.toString post.id
          AuthorId    = WebLogUserId.toString post.authorId
          Status      = PostStatus.toString   post.status
          Title       = post.title
          Permalink   = Permalink.toString post.permalink
          PublishedOn = post.publishedOn |> Option.map inTZ |> Option.toNullable
          UpdatedOn   = inTZ post.updatedOn
          Text        = if extra = "" then post.text else post.text.Replace ("href=\"/", $"href=\"{extra}/")
          CategoryIds = post.categoryIds |> List.map CategoryId.toString
          Tags        = post.tags
          Episode     = post.episode
          Metadata    = post.metadata
        }


/// View model for displaying posts
type PostDisplay =
    {   /// The posts to be displayed
        Posts : PostListItem[]
        
        /// Author ID -> name lookup
        Authors : MetaItem list
        
        /// A subtitle for the page
        Subtitle : string option
        
        /// The link to view newer (more recent) posts
        NewerLink : string option
        
        /// The name of the next newer post (single-post only)
        NewerName : string option
        
        /// The link to view older (less recent) posts
        OlderLink : string option
        
        /// The name of the next older post (single-post only)
        OlderName : string option
    }


/// View model for editing web log settings
[<CLIMutable; NoComparison; NoEquality>]
type SettingsModel =
    {   /// The name of the web log
        Name : string

        /// The slug of the web log
        Slug : string
        
        /// The subtitle of the web log
        Subtitle : string

        /// The default page
        DefaultPage : string

        /// How many posts should appear on index pages
        PostsPerPage : int

        /// The time zone in which dates/times should be displayed
        TimeZone : string
        
        /// The theme to use to display the web log
        ThemePath : string
        
        /// Whether to automatically load htmx
        AutoHtmx : bool
        
        /// The default location for uploads
        Uploads : string
    }
    
    /// Create a settings model from a web log
    static member fromWebLog (webLog : WebLog) =
        { Name         = webLog.name
          Slug         = webLog.slug
          Subtitle     = defaultArg webLog.subtitle ""
          DefaultPage  = webLog.defaultPage
          PostsPerPage = webLog.postsPerPage
          TimeZone     = webLog.timeZone
          ThemePath    = webLog.themePath
          AutoHtmx     = webLog.autoHtmx
          Uploads      = UploadDestination.toString webLog.uploads
        }
    
    /// Update a web log with settings from the form
    member this.update (webLog : WebLog) =
        { webLog with
            name         = this.Name
            slug         = this.Slug
            subtitle     = if this.Subtitle = "" then None else Some this.Subtitle
            defaultPage  = this.DefaultPage
            postsPerPage = this.PostsPerPage
            timeZone     = this.TimeZone
            themePath    = this.ThemePath
            autoHtmx     = this.AutoHtmx
            uploads      = UploadDestination.parse this.Uploads
        }


/// View model for uploading a file
[<CLIMutable; NoComparison; NoEquality>]
type UploadFileModel =
    {   /// The upload destination
        Destination : string
    }


/// A message displayed to the user
[<CLIMutable; NoComparison; NoEquality>]
type UserMessage =
    {   /// The level of the message
        Level : string
        
        /// The message
        Message : string
        
        /// Further details about the message
        Detail : string option
    }

/// Functions to support user messages
module UserMessage =
    
    /// An empty user message (use one of the others for pre-filled level)
    let empty = { Level = ""; Message = ""; Detail = None }
    
    /// A blank success message
    let success = { empty with Level = "success" }
    
    /// A blank informational message
    let info = { empty with Level = "primary" }
    
    /// A blank warning message
    let warning = { empty with Level = "warning" }
    
    /// A blank error message
    let error = { empty with Level = "danger" }
