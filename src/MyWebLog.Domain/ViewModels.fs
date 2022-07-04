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
        posts : int

        /// The number of post drafts
        drafts : int

        /// The number of pages
        pages : int

        /// The number of pages in the page list
        listedPages : int

        /// The number of categories
        categories : int

        /// The top-level categories
        topLevelCategories : int
    }


/// Details about a category, used to display category lists
[<NoComparison; NoEquality>]
type DisplayCategory =
    {   /// The ID of the category
        id : string
        
        /// The slug for the category
        slug : string
        
        /// The name of the category
        name : string
        
        /// A description of the category
        description : string option
        
        /// The parent category names for this (sub)category
        parentNames : string[]
        
        /// The number of posts in this category
        postCount : int
    }


/// A display version of a custom feed definition
type DisplayCustomFeed =
    {   /// The ID of the custom feed
        id : string
        
        /// The source of the custom feed
        source : string
        
        /// The relative path at which the custom feed is served
        path : string
        
        /// Whether this custom feed is for a podcast
        isPodcast : bool
    }
    
    /// Create a display version from a custom feed
    static member fromFeed (cats : DisplayCategory[]) (feed : CustomFeed) : DisplayCustomFeed =
        let source =
            match feed.source with
            | Category (CategoryId catId) -> $"Category: {(cats |> Array.find (fun cat -> cat.id = catId)).name}"
            | Tag tag -> $"Tag: {tag}"
        { id        = CustomFeedId.toString feed.id
          source    = source
          path      = Permalink.toString feed.path
          isPodcast = Option.isSome feed.podcast
        }


/// Details about a page used to display page lists
[<NoComparison; NoEquality>]
type DisplayPage =
    {   /// The ID of this page
        id : string

        /// The title of the page
        title : string

        /// The link at which this page is displayed
        permalink : string

        /// When this page was published
        publishedOn : DateTime

        /// When this page was last updated
        updatedOn : DateTime

        /// Whether this page shows as part of the web log's navigation
        showInPageList : bool
        
        /// Is this the default page?
        isDefault : bool
        
        /// The text of the page
        text : string
        
        /// The metadata for the page
        metadata : MetaItem list
    }
    
    /// Create a minimal display page (no text or metadata) from a database page
    static member fromPageMinimal webLog (page : Page) =
        let pageId = PageId.toString page.id
        { id             = pageId
          title          = page.title
          permalink      = Permalink.toString page.permalink
          publishedOn    = page.publishedOn
          updatedOn      = page.updatedOn
          showInPageList = page.showInPageList
          isDefault      = pageId = webLog.defaultPage
          text           = ""
          metadata       = []
        }
    
    /// Create a display page from a database page
    static member fromPage webLog (page : Page) =
        let _, extra = WebLog.hostAndPath webLog
        let pageId = PageId.toString page.id
        { id             = pageId
          title          = page.title
          permalink      = Permalink.toString page.permalink
          publishedOn    = page.publishedOn
          updatedOn      = page.updatedOn
          showInPageList = page.showInPageList
          isDefault      = pageId = webLog.defaultPage
          text           = if extra = "" then page.text else page.text.Replace ("href=\"/", $"href=\"{extra}/")
          metadata       = page.metadata
        }


open System.IO

/// Information about an uploaded file used for display
[<NoComparison; NoEquality>]
type DisplayUpload =
    {   /// The ID of the uploaded file
        id : string
        
        /// The name of the uploaded file
        name : string
        
        /// The path at which the file is served
        path : string
        
        /// The date/time the file was updated
        updatedOn : DateTime option
        
        /// The source for this file (created from UploadDestination DU)
        source : string
    }
    
    /// Create a display uploaded file
    static member fromUpload webLog source (upload : Upload) =
        let path = Permalink.toString upload.path
        let name = Path.GetFileName path
        { id        = UploadId.toString upload.id
          name      = name
          path      = path.Replace (name, "")
          updatedOn = Some (WebLog.localTime webLog upload.updatedOn)
          source    = UploadDestination.toString source
        }


/// View model for editing categories
[<CLIMutable; NoComparison; NoEquality>]
type EditCategoryModel =
    {   /// The ID of the category being edited
        categoryId : string
        
        /// The name of the category
        name : string
        
        /// The category's URL slug
        slug : string
        
        /// A description of the category (optional)
        description : string
        
        /// The ID of the category for which this is a subcategory (optional)
        parentId : string
    }
    
    /// Create an edit model from an existing category 
    static member fromCategory (cat : Category) =
        { categoryId  = CategoryId.toString cat.id
          name        = cat.name
          slug        = cat.slug
          description = defaultArg cat.description ""
          parentId    = cat.parentId |> Option.map CategoryId.toString |> Option.defaultValue ""
        }


/// View model to edit a custom RSS feed
[<CLIMutable; NoComparison; NoEquality>]
type EditCustomFeedModel =
    {   /// The ID of the feed being editing
        id : string
        
        /// The type of source for this feed ("category" or "tag")
        sourceType : string
        
        /// The category ID or tag on which this feed is based
        sourceValue : string
        
        /// The relative path at which this feed is served
        path : string
        
        /// Whether this feed defines a podcast
        isPodcast : bool
        
        /// The title of the podcast
        title : string
        
        /// A subtitle for the podcast
        subtitle : string
        
        /// The number of items in the podcast feed
        itemsInFeed : int
        
        /// A summary of the podcast (iTunes field)
        summary : string
        
        /// The display name of the podcast author (iTunes field)
        displayedAuthor : string
        
        /// The e-mail address of the user who registered the podcast at iTunes
        email : string
        
        /// The link to the image for the podcast
        imageUrl : string
        
        /// The category from iTunes under which this podcast is categorized
        itunesCategory : string
        
        /// A further refinement of the categorization of this podcast (iTunes field / values)
        itunesSubcategory : string
        
        /// The explictness rating (iTunes field)
        explicit : string
        
        /// The default media type for files in this podcast
        defaultMediaType : string
        
        /// The base URL for relative URL media files for this podcast (optional; defaults to web log base)
        mediaBaseUrl : string
        
        /// The URL for funding information for the podcast
        fundingUrl : string
        
        /// The text for the funding link
        fundingText : string
        
        /// A unique identifier to follow this podcast
        guid : string
        
        /// The medium for the content of this podcast
        medium : string
    }
    
    /// An empty custom feed model
    static member empty =
        { id                = ""
          sourceType        = "category"
          sourceValue       = ""
          path              = ""
          isPodcast         = false
          title             = ""
          subtitle          = ""
          itemsInFeed       = 25
          summary           = ""
          displayedAuthor   = ""
          email             = ""
          imageUrl          = ""
          itunesCategory    = ""
          itunesSubcategory = ""
          explicit          = "no"
          defaultMediaType  = "audio/mpeg"
          mediaBaseUrl      = ""
          fundingUrl        = ""
          fundingText       = ""
          guid              = ""
          medium            = ""
        }
    
    /// Create a model from a custom feed
    static member fromFeed (feed : CustomFeed) =
        let rss =
            { EditCustomFeedModel.empty with
                id          = CustomFeedId.toString feed.id
                sourceType  = match feed.source with Category _ -> "category" | Tag _ -> "tag"
                sourceValue = match feed.source with Category (CategoryId catId) -> catId | Tag tag -> tag
                path        = Permalink.toString feed.path
            }
        match feed.podcast with
        | Some p ->
            { rss with
                isPodcast         = true
                title             = p.title
                subtitle          = defaultArg p.subtitle ""
                itemsInFeed       = p.itemsInFeed
                summary           = p.summary
                displayedAuthor   = p.displayedAuthor
                email             = p.email
                imageUrl          = Permalink.toString p.imageUrl
                itunesCategory    = p.iTunesCategory
                itunesSubcategory = defaultArg p.iTunesSubcategory ""
                explicit          = ExplicitRating.toString p.explicit
                defaultMediaType  = defaultArg p.defaultMediaType ""
                mediaBaseUrl      = defaultArg p.mediaBaseUrl ""
                fundingUrl        = defaultArg p.fundingUrl ""
                fundingText       = defaultArg p.fundingText ""
                guid              = p.guid
                                    |> Option.map (fun it -> it.ToString().ToLowerInvariant ())
                                    |> Option.defaultValue ""
                medium            = p.medium |> Option.map PodcastMedium.toString |> Option.defaultValue ""
            }
        | None -> rss
    
    /// Update a feed with values from this model
    member this.updateFeed (feed : CustomFeed) =
        { feed with
            source  = if this.sourceType = "tag" then Tag this.sourceValue else Category (CategoryId this.sourceValue)
            path    = Permalink this.path
            podcast =
                if this.isPodcast then
                    Some {
                        title             = this.title
                        subtitle          = noneIfBlank this.subtitle
                        itemsInFeed       = this.itemsInFeed
                        summary           = this.summary
                        displayedAuthor   = this.displayedAuthor
                        email             = this.email
                        imageUrl          = Permalink this.imageUrl
                        iTunesCategory    = this.itunesCategory
                        iTunesSubcategory = noneIfBlank this.itunesSubcategory
                        explicit          = ExplicitRating.parse this.explicit
                        defaultMediaType  = noneIfBlank this.defaultMediaType
                        mediaBaseUrl      = noneIfBlank this.mediaBaseUrl
                        guid              = noneIfBlank this.guid |> Option.map Guid.Parse
                        fundingUrl        = noneIfBlank this.fundingUrl
                        fundingText       = noneIfBlank this.fundingText
                        medium            = noneIfBlank this.medium |> Option.map PodcastMedium.parse
                    }
                else
                    None
        }

/// View model to edit a page
[<CLIMutable; NoComparison; NoEquality>]
type EditPageModel =
    {   /// The ID of the page being edited
        pageId : string

        /// The title of the page
        title : string

        /// The permalink for the page
        permalink : string

        /// The template to use to display the page
        template : string
        
        /// Whether this page is shown in the page list
        isShownInPageList : bool

        /// The source format for the text
        source : string

        /// The text of the page
        text : string
        
        /// Names of metadata items
        metaNames : string[]
        
        /// Values of metadata items
        metaValues : string[]
    }
    
    /// Create an edit model from an existing page
    static member fromPage (page : Page) =
        let latest =
            match page.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let page = if page.metadata |> List.isEmpty then { page with metadata = [ MetaItem.empty ] } else page
        { pageId            = PageId.toString page.id
          title             = page.title
          permalink         = Permalink.toString page.permalink
          template          = defaultArg page.template ""
          isShownInPageList = page.showInPageList
          source            = MarkupText.sourceType latest.text
          text              = MarkupText.text       latest.text
          metaNames         = page.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          metaValues        = page.metadata |> List.map (fun m -> m.value) |> Array.ofList
        }


/// View model to edit a post
[<CLIMutable; NoComparison; NoEquality>]
type EditPostModel =
    {   /// The ID of the post being edited
        postId : string

        /// The title of the post
        title : string

        /// The permalink for the post
        permalink : string

        /// The source format for the text
        source : string

        /// The text of the post
        text : string
        
        /// The tags for the post
        tags : string
        
        /// The template used to display the post
        template : string
        
        /// The category IDs for the post
        categoryIds : string[]
        
        /// The post status
        status : string
        
        /// Whether this post should be published
        doPublish : bool
        
        /// Names of metadata items
        metaNames : string[]
        
        /// Values of metadata items
        metaValues : string[]
        
        /// Whether to override the published date/time
        setPublished : bool
        
        /// The published date/time to override
        pubOverride : Nullable<DateTime>
        
        /// Whether all revisions should be purged and the override date set as the updated date as well
        setUpdated : bool
        
        /// Whether this post has a podcast episode
        isEpisode : bool
        
        /// The URL for the media for this episode (may be permalink)
        media : string
        
        /// The size (in bytes) of the media for this episode
        length : int64
        
        /// The duration of the media for this episode
        duration : string
        
        /// The media type (optional, defaults to podcast-defined media type)
        mediaType : string
        
        /// The URL for the image for this episode (may be permalink; optional, defaults to podcast image)
        imageUrl : string
        
        /// A subtitle for the episode (optional)
        subtitle : string
        
        /// The explicit rating for this episode (optional, defaults to podcast setting)
        explicit : string
        
        /// The URL for the chapter file for the episode (may be permalink; optional)
        chapterFile : string
        
        /// The type of the chapter file (optional; defaults to application/json+chapters if chapterFile is provided)
        chapterType : string
        
        /// The URL for the transcript (may be permalink; optional)
        transcriptUrl : string
        
        /// The MIME type for the transcript (optional, recommended if transcriptUrl is provided)
        transcriptType : string
        
        /// The language of the transcript (optional)
        transcriptLang : string
        
        /// Whether the provided transcript should be presented as captions
        transcriptCaptions : bool
        
        /// The season number (optional)
        seasonNumber : int
        
        /// A description of this season (optional, ignored if season number is not provided)
        seasonDescription : string
        
        /// The episode number (decimal; optional)
        episodeNumber : string
        
        /// A description of this episode (optional, ignored if episode number is not provided)
        episodeDescription : string
    }
    
    /// Create an edit model from an existing past
    static member fromPost webLog (post : Post) =
        let latest =
            match post.revisions |> List.sortByDescending (fun r -> r.asOf) |> List.tryHead with
            | Some rev -> rev
            | None -> Revision.empty
        let post = if post.metadata |> List.isEmpty then { post with metadata = [ MetaItem.empty ] } else post
        let episode = defaultArg post.episode Episode.empty
        { postId             = PostId.toString post.id
          title              = post.title
          permalink          = Permalink.toString post.permalink
          source             = MarkupText.sourceType latest.text
          text               = MarkupText.text       latest.text
          tags               = String.Join (", ", post.tags)
          template           = defaultArg post.template ""
          categoryIds        = post.categoryIds |> List.map CategoryId.toString |> Array.ofList
          status             = PostStatus.toString post.status
          doPublish          = false
          metaNames          = post.metadata |> List.map (fun m -> m.name)  |> Array.ofList
          metaValues         = post.metadata |> List.map (fun m -> m.value) |> Array.ofList
          setPublished       = false
          pubOverride        = post.publishedOn |> Option.map (WebLog.localTime webLog) |> Option.toNullable
          setUpdated         = false
          isEpisode          = Option.isSome post.episode
          media              = episode.media
          length             = episode.length
          duration           = defaultArg (episode.duration |> Option.map (fun it -> it.ToString """hh\:mm\:ss""")) ""
          mediaType          = defaultArg episode.mediaType ""
          imageUrl           = defaultArg episode.imageUrl ""
          subtitle           = defaultArg episode.subtitle ""
          explicit           = defaultArg (episode.explicit |> Option.map ExplicitRating.toString) ""
          chapterFile        = defaultArg episode.chapterFile ""
          chapterType        = defaultArg episode.chapterType ""
          transcriptUrl      = defaultArg episode.transcriptUrl ""
          transcriptType     = defaultArg episode.transcriptType ""
          transcriptLang     = defaultArg episode.transcriptLang ""
          transcriptCaptions = defaultArg episode.transcriptCaptions false
          seasonNumber       = defaultArg episode.seasonNumber 0
          seasonDescription  = defaultArg episode.seasonDescription ""
          episodeNumber      = defaultArg (episode.episodeNumber |> Option.map string) ""  
          episodeDescription = defaultArg episode.episodeDescription ""
        }
    
    /// Update a post with values from the submitted form
    member this.updatePost (post : Post) (revision : Revision) now =
        { post with
            title       = this.title
            permalink   = Permalink this.permalink
            publishedOn = if this.doPublish then Some now else post.publishedOn
            updatedOn   = now
            text        = MarkupText.toHtml revision.text
            tags        = this.tags.Split ","
                          |> Seq.ofArray
                          |> Seq.map (fun it -> it.Trim().ToLower ())
                          |> Seq.filter (fun it -> it <> "")
                          |> Seq.sort
                          |> List.ofSeq
            template    = match this.template.Trim () with "" -> None | tmpl -> Some tmpl
            categoryIds = this.categoryIds |> Array.map CategoryId |> List.ofArray
            status      = if this.doPublish then Published else post.status
            metadata    = Seq.zip this.metaNames this.metaValues
                          |> Seq.filter (fun it -> fst it > "")
                          |> Seq.map (fun it -> { name = fst it; value = snd it })
                          |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                          |> List.ofSeq
            revisions   = match post.revisions |> List.tryHead with
                          | Some r when r.text = revision.text -> post.revisions
                          | _ -> revision :: post.revisions
            episode     =
                if this.isEpisode then
                    Some {
                        media              = this.media
                        length             = this.length
                        duration           = noneIfBlank this.duration |> Option.map TimeSpan.Parse
                        mediaType          = noneIfBlank this.mediaType
                        imageUrl           = noneIfBlank this.imageUrl
                        subtitle           = noneIfBlank this.subtitle
                        explicit           = noneIfBlank this.explicit |> Option.map ExplicitRating.parse
                        chapterFile        = noneIfBlank this.chapterFile
                        chapterType        = noneIfBlank this.chapterType
                        transcriptUrl      = noneIfBlank this.transcriptUrl
                        transcriptType     = noneIfBlank this.transcriptType
                        transcriptLang     = noneIfBlank this.transcriptLang
                        transcriptCaptions = if this.transcriptCaptions then Some true else None
                        seasonNumber       = if this.seasonNumber = 0 then None else Some this.seasonNumber
                        seasonDescription  = noneIfBlank this.seasonDescription
                        episodeNumber      = match noneIfBlank this.episodeNumber |> Option.map Double.Parse with
                                             | Some it when it = 0.0 -> None
                                             | Some it -> Some (double it)
                                             | None -> None
                        episodeDescription = noneIfBlank this.episodeDescription
                    }
                else
                    None
        }


/// View model to edit RSS settings
[<CLIMutable; NoComparison; NoEquality>]
type EditRssModel =
    {   /// Whether the site feed of posts is enabled
        feedEnabled : bool
        
        /// The name of the file generated for the site feed
        feedName : string
        
        /// Override the "posts per page" setting for the site feed
        itemsInFeed : int
        
        /// Whether feeds are enabled for all categories
        categoryEnabled : bool
        
        /// Whether feeds are enabled for all tags
        tagEnabled : bool
        
        /// A copyright string to be placed in all feeds
        copyright : string
    }
    
    /// Create an edit model from a set of RSS options
    static member fromRssOptions (rss : RssOptions) =
        { feedEnabled     = rss.feedEnabled
          feedName        = rss.feedName
          itemsInFeed     = defaultArg rss.itemsInFeed 0
          categoryEnabled = rss.categoryEnabled
          tagEnabled      = rss.tagEnabled
          copyright       = defaultArg rss.copyright ""
        }
    
    /// Update RSS options from values in this mode
    member this.updateOptions (rss : RssOptions) =
        { rss with
            feedEnabled     = this.feedEnabled
            feedName        = this.feedName
            itemsInFeed     = if this.itemsInFeed = 0 then None else Some this.itemsInFeed
            categoryEnabled = this.categoryEnabled
            tagEnabled      = this.tagEnabled
            copyright       = noneIfBlank this.copyright
        }


/// View model to edit a tag mapping
[<CLIMutable; NoComparison; NoEquality>]
type EditTagMapModel =
    {   /// The ID of the tag mapping being edited
        id : string
        
        /// The tag being mapped to a different link value
        tag : string
        
        /// The link value for the tag
        urlValue : string
    }
    
    /// Whether this is a new tag mapping
    member this.isNew = this.id = "new"
    
    /// Create an edit model from the tag mapping
    static member fromMapping (tagMap : TagMap) : EditTagMapModel =
        { id       = TagMapId.toString tagMap.id
          tag      = tagMap.tag
          urlValue = tagMap.urlValue
        }


/// View model to edit a user
[<CLIMutable; NoComparison; NoEquality>]
type EditUserModel =
    {   /// The user's first name
        firstName : string
        
        /// The user's last name
        lastName : string
        
        /// The user's preferred name
        preferredName : string
        
        /// A new password for the user
        newPassword : string
        
        /// A new password for the user, confirmed
        newPasswordConfirm : string
    }
    /// Create an edit model from a user
    static member fromUser (user : WebLogUser) =
        { firstName          = user.firstName
          lastName           = user.lastName
          preferredName      = user.preferredName
          newPassword        = ""
          newPasswordConfirm = ""
        }


/// The model to use to allow a user to log on
[<CLIMutable; NoComparison; NoEquality>]
type LogOnModel =
    {   /// The user's e-mail address
        emailAddress : string
    
        /// The user's password
        password : string
        
        /// Where the user should be redirected once they have logged on
        returnTo : string option
    }
    
    /// An empty log on model
    static member empty =
        { emailAddress = ""; password = ""; returnTo = None }


/// View model to manage permalinks
[<CLIMutable; NoComparison; NoEquality>]
type ManagePermalinksModel =
    {   /// The ID for the entity being edited
        id : string
        
        /// The type of entity being edited ("page" or "post")
        entity : string
        
        /// The current title of the page or post
        currentTitle : string
        
        /// The current permalink of the page or post
        currentPermalink : string
        
        /// The prior permalinks for the page or post
        prior : string[]
    }
    
    /// Create a permalink model from a page
    static member fromPage (pg : Page) =
        { id               = PageId.toString pg.id
          entity           = "page"
          currentTitle     = pg.title
          currentPermalink = Permalink.toString pg.permalink
          prior            = pg.priorPermalinks |> List.map Permalink.toString |> Array.ofList
        }

    /// Create a permalink model from a post
    static member fromPost (post : Post) =
        { id               = PostId.toString post.id
          entity           = "post"
          currentTitle     = post.title
          currentPermalink = Permalink.toString post.permalink
          prior            = post.priorPermalinks |> List.map Permalink.toString |> Array.ofList
        }


/// View model for posts in a list
[<NoComparison; NoEquality>]
type PostListItem =
    {   /// The ID of the post
        id : string
        
        /// The ID of the user who authored the post
        authorId : string
        
        /// The status of the post
        status : string
        
        /// The title of the post
        title : string
        
        /// The permalink for the post
        permalink : string
        
        /// When this post was published
        publishedOn : Nullable<DateTime>
        
        /// When this post was last updated
        updatedOn : DateTime
        
        /// The text of the post
        text : string
        
        /// The IDs of the categories for this post
        categoryIds : string list
        
        /// Tags for the post
        tags : string list
        
        /// The podcast episode information for this post
        episode : Episode option
        
        /// Metadata for the post
        metadata : MetaItem list
    }

    /// Create a post list item from a post
    static member fromPost (webLog : WebLog) (post : Post) =
        let _, extra = WebLog.hostAndPath webLog
        let inTZ     = WebLog.localTime   webLog
        { id          = PostId.toString post.id
          authorId    = WebLogUserId.toString post.authorId
          status      = PostStatus.toString   post.status
          title       = post.title
          permalink   = Permalink.toString post.permalink
          publishedOn = post.publishedOn |> Option.map inTZ |> Option.toNullable
          updatedOn   = inTZ post.updatedOn
          text        = if extra = "" then post.text else post.text.Replace ("href=\"/", $"href=\"{extra}/")
          categoryIds = post.categoryIds |> List.map CategoryId.toString
          tags        = post.tags
          episode     = post.episode
          metadata    = post.metadata
        }


/// View model for displaying posts
type PostDisplay =
    {   /// The posts to be displayed
        posts : PostListItem[]
        
        /// Author ID -> name lookup
        authors : MetaItem list
        
        /// A subtitle for the page
        subtitle : string option
        
        /// The link to view newer (more recent) posts
        newerLink : string option
        
        /// The name of the next newer post (single-post only)
        newerName : string option
        
        /// The link to view older (less recent) posts
        olderLink : string option
        
        /// The name of the next older post (single-post only)
        olderName : string option
    }


/// View model for editing web log settings
[<CLIMutable; NoComparison; NoEquality>]
type SettingsModel =
    {   /// The name of the web log
        name : string

        /// The subtitle of the web log
        subtitle : string

        /// The default page
        defaultPage : string

        /// How many posts should appear on index pages
        postsPerPage : int

        /// The time zone in which dates/times should be displayed
        timeZone : string
        
        /// The theme to use to display the web log
        themePath : string
        
        /// Whether to automatically load htmx
        autoHtmx : bool
    }
    
    /// Create a settings model from a web log
    static member fromWebLog (webLog : WebLog) =
        { name         = webLog.name
          subtitle     = defaultArg webLog.subtitle ""
          defaultPage  = webLog.defaultPage
          postsPerPage = webLog.postsPerPage
          timeZone     = webLog.timeZone
          themePath    = webLog.themePath
          autoHtmx     = webLog.autoHtmx
        }
    
    /// Update a web log with settings from the form
    member this.update (webLog : WebLog) =
        { webLog with
            name         = this.name
            subtitle     = if this.subtitle = "" then None else Some this.subtitle
            defaultPage  = this.defaultPage
            postsPerPage = this.postsPerPage
            timeZone     = this.timeZone
            themePath    = this.themePath
            autoHtmx     = this.autoHtmx
        }


/// View model for uploading a file
[<CLIMutable; NoComparison; NoEquality>]
type UploadFileModel =
    {   /// The upload destination
        destination : string
    }


/// A message displayed to the user
[<CLIMutable; NoComparison; NoEquality>]
type UserMessage =
    {   /// The level of the message
        level : string
        
        /// The message
        message : string
        
        /// Further details about the message
        detail : string option
    }

/// Functions to support user messages
module UserMessage =
    
    /// An empty user message (use one of the others for pre-filled level)
    let empty = { level = ""; message = ""; detail = None }
    
    /// A blank success message
    let success = { empty with level = "success" }
    
    /// A blank informational message
    let info = { empty with level = "primary" }
    
    /// A blank warning message
    let warning = { empty with level = "warning" }
    
    /// A blank error message
    let error = { empty with level = "danger" }
