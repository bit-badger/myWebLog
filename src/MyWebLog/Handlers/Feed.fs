/// Functions to support generating RSS feeds
module MyWebLog.Handlers.Feed

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.ServiceModel.Syndication
open System.Text.RegularExpressions
open System.Xml
open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog
open MyWebLog.ViewModels

// ~~ FEED GENERATION ~~

/// The type of feed to generate
type FeedType =
    | StandardFeed of string
    | CategoryFeed of CategoryId * string
    | TagFeed      of string * string
    | Custom       of CustomFeed * string

/// Derive the type of RSS feed requested
let deriveFeedType (ctx : HttpContext) feedPath : (FeedType * int) option =
    let webLog    = ctx.WebLog
    let debug     = debug "Feed" ctx
    let name      = $"/{webLog.rss.feedName}"
    let postCount = defaultArg webLog.rss.itemsInFeed webLog.postsPerPage
    debug (fun () -> $"Considering potential feed for {feedPath} (configured feed name {name})")
    // Standard feed
    match webLog.rss.feedEnabled && feedPath = name with
    | true  ->
        debug (fun () -> "Found standard feed")
        Some (StandardFeed feedPath, postCount)
    | false ->
        // Category and tag feeds are handled by defined routes; check for custom feed
        match webLog.rss.customFeeds
              |> List.tryFind (fun it -> feedPath.EndsWith (Permalink.toString it.path)) with
        | Some feed ->
            debug (fun () -> "Found custom feed")
            Some (Custom (feed, feedPath),
                  feed.podcast |> Option.map (fun p -> p.itemsInFeed) |> Option.defaultValue postCount)
        | None ->
            debug (fun () -> $"No matching feed found")
            None

/// Determine the function to retrieve posts for the given feed
let private getFeedPosts ctx feedType =
    let childIds catId =
        let cat = CategoryCache.get ctx |> Array.find (fun c -> c.id = CategoryId.toString catId)
        getCategoryIds cat.slug ctx
    let data = ctx.Data
    match feedType with
    | StandardFeed _          -> data.Post.findPageOfPublishedPosts ctx.WebLog.id 1
    | CategoryFeed (catId, _) -> data.Post.findPageOfCategorizedPosts ctx.WebLog.id (childIds catId) 1
    | TagFeed      (tag,   _) -> data.Post.findPageOfTaggedPosts ctx.WebLog.id tag 1
    | Custom       (feed,  _) ->
        match feed.source with
        | Category catId -> data.Post.findPageOfCategorizedPosts ctx.WebLog.id (childIds catId) 1
        | Tag      tag   -> data.Post.findPageOfTaggedPosts ctx.WebLog.id tag 1

/// Strip HTML from a string
let private stripHtml text = WebUtility.HtmlDecode <| Regex.Replace (text, "<(.|\n)*?>", "")

/// XML namespaces for building RSS feeds
[<RequireQualifiedAccess>]
module private Namespace = 
    
    /// Enables encoded (HTML) content
    let content = "http://purl.org/rss/1.0/modules/content/"

    /// The dc XML namespace
    let dc = "http://purl.org/dc/elements/1.1/"

    /// iTunes elements
    let iTunes = "http://www.itunes.com/dtds/podcast-1.0.dtd"

    /// Podcast Index (AKA "podcasting 2.0")
    let podcast = "https://podcastindex.org/namespace/1.0"
    
    /// Enables chapters
    let psc = "http://podlove.org/simple-chapters/"

    /// Enables another "subscribe" option
    let rawVoice = "http://www.rawvoice.com/rawvoiceRssModule/"

/// Create a feed item from the given post    
let private toFeedItem webLog (authors : MetaItem list) (cats : DisplayCategory[]) (tagMaps : TagMap list)
            (post : Post) =
    let plainText =
        let endingP = post.text.IndexOf "</p>"
        stripHtml <| if endingP >= 0 then post.text[..(endingP - 1)] else post.text
    let item = SyndicationItem (
        Id              = WebLog.absoluteUrl webLog post.permalink,
        Title           = TextSyndicationContent.CreateHtmlContent post.title,
        PublishDate     = DateTimeOffset post.publishedOn.Value,
        LastUpdatedTime = DateTimeOffset post.updatedOn,
        Content         = TextSyndicationContent.CreatePlaintextContent plainText)
    item.AddPermalink (Uri item.Id)
    
    let xmlDoc = XmlDocument ()
    
    let encoded =
        let txt =
            post.text
                .Replace("src=\"/", $"src=\"{webLog.urlBase}/")
                .Replace ("href=\"/", $"href=\"{webLog.urlBase}/")
        let it  = xmlDoc.CreateElement ("content", "encoded", Namespace.content)
        let _   = it.AppendChild (xmlDoc.CreateCDataSection txt)
        it
    item.ElementExtensions.Add encoded
        
    item.Authors.Add (SyndicationPerson (
        Name = (authors |> List.find (fun a -> a.name = WebLogUserId.toString post.authorId)).value))
    [ post.categoryIds
      |> List.map (fun catId ->
          let cat = cats |> Array.find (fun c -> c.id = CategoryId.toString catId)
          SyndicationCategory (cat.name, WebLog.absoluteUrl webLog (Permalink $"category/{cat.slug}/"), cat.name))
      post.tags
      |> List.map (fun tag ->
          let urlTag =
              match tagMaps |> List.tryFind (fun tm -> tm.tag = tag) with
              | Some tm -> tm.urlValue
              | None -> tag.Replace (" ", "+")
          SyndicationCategory (tag, WebLog.absoluteUrl webLog (Permalink $"tag/{urlTag}/"), $"{tag} (tag)"))
    ]
    |> List.concat
    |> List.iter item.Categories.Add
    item

/// Convert non-absolute URLs to an absolute URL for this web log
let toAbsolute webLog (link : string) =
    if link.StartsWith "http" then link else WebLog.absoluteUrl webLog (Permalink link)

/// Add episode information to a podcast feed item
let private addEpisode webLog (podcast : PodcastOptions) (episode : Episode) (post : Post) (item : SyndicationItem) =
    let epMediaUrl =
        match episode.media with
        | link when link.StartsWith "http" -> link
        | link when Option.isSome podcast.mediaBaseUrl -> $"{podcast.mediaBaseUrl.Value}{link}"
        | link -> WebLog.absoluteUrl webLog (Permalink link)
    let epMediaType = [ episode.mediaType; podcast.defaultMediaType ] |> List.tryFind Option.isSome |> Option.flatten
    let epImageUrl = defaultArg episode.imageUrl (Permalink.toString podcast.imageUrl) |> toAbsolute webLog
    let epExplicit = defaultArg episode.explicit podcast.explicit |> ExplicitRating.toString
    
    let xmlDoc    = XmlDocument ()
    let enclosure =
        let it = xmlDoc.CreateElement "enclosure"
        it.SetAttribute ("url", epMediaUrl)
        it.SetAttribute ("length", string episode.length)
        epMediaType |> Option.iter (fun typ -> it.SetAttribute ("type", typ))
        it
    let image =
        let it = xmlDoc.CreateElement ("itunes", "image", Namespace.iTunes)
        it.SetAttribute ("href", epImageUrl)
        it
        
    item.ElementExtensions.Add enclosure
    item.ElementExtensions.Add image
    item.ElementExtensions.Add ("creator",  Namespace.dc,     podcast.displayedAuthor)
    item.ElementExtensions.Add ("author",   Namespace.iTunes, podcast.displayedAuthor)
    item.ElementExtensions.Add ("explicit", Namespace.iTunes, epExplicit)
    episode.subtitle |> Option.iter (fun it -> item.ElementExtensions.Add ("subtitle", Namespace.iTunes, it))
    episode.duration
    |> Option.iter (fun it -> item.ElementExtensions.Add ("duration", Namespace.iTunes, it.ToString """hh\:mm\:ss"""))
    
    match episode.chapterFile with
    | Some chapters ->
        let url = toAbsolute webLog chapters
        let typ =
            match episode.chapterType with
            | Some mime -> Some mime
            | None when chapters.EndsWith ".json" -> Some "application/json+chapters"
            | None -> None
        let elt = xmlDoc.CreateElement ("podcast", "chapters", Namespace.podcast)
        elt.SetAttribute ("url", url)
        typ |> Option.iter (fun it -> elt.SetAttribute ("type", it))
        item.ElementExtensions.Add elt
    | None -> ()
    
    match episode.transcriptUrl with
    | Some transcript ->
        let url = toAbsolute webLog transcript
        let elt = xmlDoc.CreateElement ("podcast", "transcript", Namespace.podcast)
        elt.SetAttribute ("url", url)
        elt.SetAttribute ("type", Option.get episode.transcriptType)
        episode.transcriptLang |> Option.iter (fun it -> elt.SetAttribute ("language", it))
        if defaultArg episode.transcriptCaptions false then
            elt.SetAttribute ("rel", "captions")
        item.ElementExtensions.Add elt
    | None -> ()
    
    match episode.seasonNumber with
    | Some season ->
        match episode.seasonDescription with
        | Some desc ->
            let elt = xmlDoc.CreateElement ("podcast", "season", Namespace.podcast)
            elt.SetAttribute ("name", desc)
            elt.InnerText <- string season
            item.ElementExtensions.Add elt
        | None -> item.ElementExtensions.Add ("season", Namespace.podcast, string season)
    | None -> ()
    
    match episode.episodeNumber with
    | Some epNumber ->
        match episode.episodeDescription with
        | Some desc ->
            let elt = xmlDoc.CreateElement ("podcast", "episode", Namespace.podcast)
            elt.SetAttribute ("name", desc)
            elt.InnerText <- string epNumber
            item.ElementExtensions.Add elt
        | None -> item.ElementExtensions.Add ("episode", Namespace.podcast, string epNumber)
    | None -> ()
    
    if post.metadata |> List.exists (fun it -> it.name = "chapter") then
        try
            let chapters = xmlDoc.CreateElement ("psc", "chapters", Namespace.psc)
            chapters.SetAttribute ("version", "1.2")
            
            post.metadata
            |> List.filter (fun it -> it.name = "chapter")
            |> List.map (fun it ->
                TimeSpan.Parse (it.value.Split(" ")[0]), it.value.Substring (it.value.IndexOf(" ") + 1))
            |> List.sortBy fst
            |> List.iter (fun chap ->
                let chapter = xmlDoc.CreateElement ("psc", "chapter", Namespace.psc)
                chapter.SetAttribute ("start", (fst chap).ToString "hh:mm:ss")
                chapter.SetAttribute ("title", snd chap)
                chapters.AppendChild chapter |> ignore)
            
            item.ElementExtensions.Add chapters
        with _ -> ()
    item
    
/// Add a namespace to the feed
let private addNamespace (feed : SyndicationFeed) alias nsUrl =
    feed.AttributeExtensions.Add (XmlQualifiedName (alias, "http://www.w3.org/2000/xmlns/"), nsUrl)

/// Add items to the top of the feed required for podcasts
let private addPodcast webLog (rssFeed : SyndicationFeed) (feed : CustomFeed) =
    let addChild (doc : XmlDocument) ns prefix name value (elt : XmlElement) =
        let child =
            if ns = "" then doc.CreateElement name else doc.CreateElement (prefix, name, ns)
            |> elt.AppendChild
        child.InnerText <- value
        elt
    
    let podcast  = Option.get feed.podcast
    let feedUrl  = WebLog.absoluteUrl webLog feed.path
    let imageUrl =
        match podcast.imageUrl with
        | Permalink link when link.StartsWith "http" -> link
        | Permalink _ -> WebLog.absoluteUrl webLog podcast.imageUrl
    
    let xmlDoc = XmlDocument ()
    
    [ "dc",       Namespace.dc
      "itunes",   Namespace.iTunes
      "podcast",  Namespace.podcast
      "psc",      Namespace.psc
      "rawvoice", Namespace.rawVoice
    ]
    |> List.iter (fun (alias, nsUrl) -> addNamespace rssFeed alias nsUrl)
    
    let categorization =
        let it = xmlDoc.CreateElement ("itunes", "category", Namespace.iTunes)
        it.SetAttribute ("text", podcast.iTunesCategory)
        podcast.iTunesSubcategory
        |> Option.iter (fun subCat ->
            let subCatElt = xmlDoc.CreateElement ("itunes", "category", Namespace.iTunes)
            subCatElt.SetAttribute ("text", subCat)
            it.AppendChild subCatElt |> ignore)
        it
    let image = 
        [ "title", podcast.title
          "url",   imageUrl
          "link",  feedUrl
        ]
        |> List.fold (fun elt (name, value) -> addChild xmlDoc "" "" name value elt) (xmlDoc.CreateElement "image")
    let iTunesImage =
        let it = xmlDoc.CreateElement ("itunes", "image", Namespace.iTunes)
        it.SetAttribute ("href", imageUrl)
        it
    let owner =
        [ "name", podcast.displayedAuthor
          "email", podcast.email
        ]
        |> List.fold (fun elt (name, value) -> addChild xmlDoc Namespace.iTunes "itunes" name value elt)
                     (xmlDoc.CreateElement ("itunes", "owner", Namespace.iTunes))
    let rawVoice =
        let it = xmlDoc.CreateElement ("rawvoice", "subscribe", Namespace.rawVoice)
        it.SetAttribute ("feed", feedUrl)
        it.SetAttribute ("itunes", "")
        it
    
    rssFeed.ElementExtensions.Add image
    rssFeed.ElementExtensions.Add owner
    rssFeed.ElementExtensions.Add categorization
    rssFeed.ElementExtensions.Add iTunesImage
    rssFeed.ElementExtensions.Add rawVoice
    rssFeed.ElementExtensions.Add ("summary",   Namespace.iTunes,   podcast.summary)
    rssFeed.ElementExtensions.Add ("author",    Namespace.iTunes,   podcast.displayedAuthor)
    rssFeed.ElementExtensions.Add ("explicit",  Namespace.iTunes,   ExplicitRating.toString podcast.explicit)
    podcast.subtitle |> Option.iter (fun sub -> rssFeed.ElementExtensions.Add ("subtitle", Namespace.iTunes, sub))
    podcast.fundingUrl
    |> Option.iter (fun url ->
        let funding = xmlDoc.CreateElement ("podcast", "funding", Namespace.podcast)
        funding.SetAttribute ("url", toAbsolute webLog url)
        funding.InnerText <- defaultArg podcast.fundingText "Support This Podcast"
        rssFeed.ElementExtensions.Add funding)
    podcast.guid
    |> Option.iter (fun guid ->
        rssFeed.ElementExtensions.Add ("guid", Namespace.podcast, guid.ToString().ToLowerInvariant ()))
    podcast.medium
    |> Option.iter (fun med -> rssFeed.ElementExtensions.Add ("medium", Namespace.podcast, PodcastMedium.toString med))

/// Get the feed's self reference and non-feed link
let private selfAndLink webLog feedType ctx =
    let withoutFeed (it : string) = Permalink (it.Replace ($"/{webLog.rss.feedName}", ""))
    match feedType with
    | StandardFeed     path
    | CategoryFeed (_, path)
    | TagFeed      (_, path) -> Permalink path[1..], withoutFeed path
    | Custom (feed, _) ->
        match feed.source with
        | Category (CategoryId catId) ->
            feed.path, Permalink $"category/{(CategoryCache.get ctx |> Array.find (fun c -> c.id = catId)).slug}"
        | Tag tag -> feed.path, Permalink $"""tag/{tag.Replace(" ", "+")}/""" 

/// Set the title and description of the feed based on its source
let private setTitleAndDescription feedType (webLog : WebLog) (cats : DisplayCategory[]) (feed : SyndicationFeed) =
    let cleanText opt def = TextSyndicationContent (stripHtml (defaultArg opt def))
    match feedType with
    | StandardFeed _ ->
        feed.Title       <- cleanText None webLog.name
        feed.Description <- cleanText webLog.subtitle webLog.name
    | CategoryFeed (CategoryId catId, _) ->
        let cat = cats |> Array.find (fun it -> it.id = catId)
        feed.Title       <- cleanText None $"""{webLog.name} - "{stripHtml cat.name}" Category"""
        feed.Description <- cleanText cat.description $"""Posts categorized under "{cat.name}" """
    | TagFeed (tag, _) ->
        feed.Title       <- cleanText None $"""{webLog.name} - "{tag}" Tag"""
        feed.Description <- cleanText None $"""Posts with the "{tag}" tag"""
    | Custom (custom, _) ->
        match custom.podcast with
        | Some podcast ->
            feed.Title       <- cleanText None podcast.title
            feed.Description <- cleanText podcast.subtitle podcast.title
        | None ->
            match custom.source with
            | Category (CategoryId catId) ->
                let cat = cats |> Array.find (fun it -> it.id = catId)
                feed.Title       <- cleanText None $"""{webLog.name} - "{stripHtml cat.name}" Category"""
                feed.Description <- cleanText cat.description $"""Posts categorized under "{cat.name}" """
            | Tag tag ->
                feed.Title       <- cleanText None $"""{webLog.name} - "{tag}" Tag"""
                feed.Description <- cleanText None $"""Posts with the "{tag}" tag"""
    
/// Create a feed with a known non-zero-length list of posts    
let createFeed (feedType : FeedType) posts : HttpHandler = fun next ctx -> backgroundTask {
    let  webLog     = ctx.WebLog
    let  data       = ctx.Data
    let! authors    = getAuthors     webLog posts data
    let! tagMaps    = getTagMappings webLog posts data
    let  cats       = CategoryCache.get ctx
    let  podcast    = match feedType with Custom (feed, _) when Option.isSome feed.podcast -> Some feed | _ -> None
    let  self, link = selfAndLink webLog feedType ctx
    
    let toItem post =
        let item = toFeedItem webLog authors cats tagMaps post
        match podcast, post.episode with
        | Some feed, Some episode -> addEpisode webLog (Option.get feed.podcast) episode post item
        | Some _, _ ->
            warn "Feed" ctx $"[{webLog.name} {Permalink.toString self}] \"{stripHtml post.title}\" has no media"
            item
        | _ -> item
        
    let feed = SyndicationFeed ()
    addNamespace feed "content" Namespace.content
    setTitleAndDescription feedType webLog cats feed
    
    feed.LastUpdatedTime <- (List.head posts).updatedOn |> DateTimeOffset
    feed.Generator       <- generator ctx
    feed.Items           <- posts |> Seq.ofList |> Seq.map toItem
    feed.Language        <- "en"
    feed.Id              <- WebLog.absoluteUrl webLog link
    webLog.rss.copyright |> Option.iter (fun copy -> feed.Copyright <- TextSyndicationContent copy)
    
    feed.Links.Add (SyndicationLink (Uri (WebLog.absoluteUrl webLog self), "self", "", "application/rss+xml", 0L))
    feed.ElementExtensions.Add ("link", "", WebLog.absoluteUrl webLog link)
    
    podcast |> Option.iter (addPodcast webLog feed)
    
    use mem = new MemoryStream ()
    use xml = XmlWriter.Create mem
    feed.SaveAsRss20 xml
    xml.Close ()
    
    let _ = mem.Seek (0L, SeekOrigin.Begin)
    let rdr = new StreamReader(mem)
    let! output = rdr.ReadToEndAsync ()
    
    return! (setHttpHeader "Content-Type" "text/xml" >=> setStatusCode 200 >=> setBodyFromString output) next ctx
}

// GET {any-prescribed-feed}
let generate (feedType : FeedType) postCount : HttpHandler = fun next ctx -> backgroundTask {
    match! getFeedPosts ctx feedType postCount with
    | posts when List.length posts > 0 -> return! createFeed feedType posts next ctx
    | _ -> return! Error.notFound next ctx
}

// ~~ FEED ADMINISTRATION ~~

open DotLiquid

// GET: /admin/settings/rss
let editSettings : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let feeds =
        webLog.rss.customFeeds
        |> List.map (DisplayCustomFeed.fromFeed (CategoryCache.get ctx))
        |> Array.ofList
    return! Hash.FromAnonymousObject {|
            page_title   = "RSS Settings"
            csrf         = ctx.CsrfTokenSet
            model        = EditRssModel.fromRssOptions webLog.rss
            custom_feeds = feeds
        |}
        |> viewForTheme "admin" "rss-settings" next ctx
}

// POST: /admin/settings/rss
let saveSettings : HttpHandler = fun next ctx -> task {
    let  data  = ctx.Data
    let! model = ctx.BindFormAsync<EditRssModel> ()
    match! data.WebLog.findById ctx.WebLog.id with
    | Some webLog ->
        let webLog = { webLog with rss = model.updateOptions webLog.rss }
        do! data.WebLog.updateRssOptions webLog
        WebLogCache.set webLog
        do! addMessage ctx { UserMessage.success with message = "RSS settings updated successfully" }
        return! redirectToGet "admin/settings/rss" next ctx
    | None -> return! Error.notFound next ctx
}

// GET: /admin/settings/rss/{id}/edit
let editCustomFeed feedId : HttpHandler = fun next ctx -> task {
    let customFeed =
        match feedId with
        | "new" -> Some { CustomFeed.empty with id = CustomFeedId "new" }
        | _     -> ctx.WebLog.rss.customFeeds |> List.tryFind (fun f -> f.id = CustomFeedId feedId)
    match customFeed with
    | Some f ->
        return! Hash.FromAnonymousObject {|
                page_title    = $"""{if feedId = "new" then "Add" else "Edit"} Custom RSS Feed"""
                csrf          = ctx.CsrfTokenSet
                model         = EditCustomFeedModel.fromFeed f
                categories    = CategoryCache.get ctx
                medium_values = [|
                    KeyValuePair.Create ("", "&ndash; Unspecified &ndash;")
                    KeyValuePair.Create (PodcastMedium.toString Podcast, "Podcast")
                    KeyValuePair.Create (PodcastMedium.toString Music, "Music")
                    KeyValuePair.Create (PodcastMedium.toString Video, "Video")
                    KeyValuePair.Create (PodcastMedium.toString Film, "Film")
                    KeyValuePair.Create (PodcastMedium.toString Audiobook, "Audiobook")
                    KeyValuePair.Create (PodcastMedium.toString Newsletter, "Newsletter")
                    KeyValuePair.Create (PodcastMedium.toString Blog, "Blog")
                |]
            |}
            |> viewForTheme "admin" "custom-feed-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST: /admin/settings/rss/save
let saveCustomFeed : HttpHandler = fun next ctx -> task {
    let data = ctx.Data
    match! data.WebLog.findById ctx.WebLog.id with
    | Some webLog ->
        let! model = ctx.BindFormAsync<EditCustomFeedModel> ()
        let theFeed =
            match model.id with
            | "new" -> Some { CustomFeed.empty with id = CustomFeedId.create () }
            | _ -> webLog.rss.customFeeds |> List.tryFind (fun it -> CustomFeedId.toString it.id = model.id)
        match theFeed with
        | Some feed ->
            let feeds = model.updateFeed feed :: (webLog.rss.customFeeds |> List.filter (fun it -> it.id <> feed.id))
            let webLog = { webLog with rss = { webLog.rss with customFeeds = feeds } }
            do! data.WebLog.updateRssOptions webLog
            WebLogCache.set webLog
            do! addMessage ctx {
                UserMessage.success with
                  message = $"""Successfully {if model.id = "new" then "add" else "sav"}ed custom feed"""
            }
            return! redirectToGet $"admin/settings/rss/{CustomFeedId.toString feed.id}/edit" next ctx
        | None -> return! Error.notFound next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/settings/rss/{id}/delete
let deleteCustomFeed feedId : HttpHandler = fun next ctx -> task {
    let data = ctx.Data
    match! data.WebLog.findById ctx.WebLog.id with
    | Some webLog ->
        let customId = CustomFeedId feedId
        if webLog.rss.customFeeds |> List.exists (fun f -> f.id = customId) then
            let webLog = {
              webLog with
                rss = {
                  webLog.rss with
                    customFeeds = webLog.rss.customFeeds |> List.filter (fun f -> f.id <> customId)
                }
            }
            do! data.WebLog.updateRssOptions webLog
            WebLogCache.set webLog
            do! addMessage ctx { UserMessage.success with message = "Custom feed deleted successfully" }
        else
            do! addMessage ctx { UserMessage.warning with message = "Custom feed not found; no action taken" }
        return! redirectToGet "admin/settings/rss" next ctx
    | None -> return! Error.notFound next ctx
}
