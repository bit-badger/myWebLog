/// Functions to support generating RSS feeds
module MyWebLog.Handlers.Feed

open System
open System.IO
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
    | StandardFeed
    | CategoryFeed of CategoryId
    | TagFeed      of string
    | Custom       of CustomFeed

/// Derive the type of RSS feed requested
let deriveFeedType (ctx : HttpContext) feedPath : (FeedType * int) option =
    let webLog    = ctx.WebLog
    let name      = $"/{webLog.rss.feedName}"
    let postCount = defaultArg webLog.rss.itemsInFeed webLog.postsPerPage
    // Standard feed
    match webLog.rss.feedEnabled && feedPath = name with
    | true  -> Some (StandardFeed, postCount)
    | false ->
        // Category feed
        match CategoryCache.get ctx |> Array.tryFind (fun cat -> cat.slug = feedPath.Replace (name, "")) with
        | Some cat -> Some (CategoryFeed (CategoryId cat.id), postCount)
        | None ->
            // Tag feed
            match feedPath.StartsWith "/tag/" with
            | true  -> Some (TagFeed (feedPath.Replace("/tag/", "").Replace(name, "")), postCount)
            | false ->
                // Custom feed
                match webLog.rss.customFeeds
                      |> List.tryFind (fun it -> (Permalink.toString it.path).EndsWith feedPath) with
                | Some feed ->
                    Some (Custom feed,
                          feed.podcast |> Option.map (fun p -> p.itemsInFeed) |> Option.defaultValue postCount)
                | None ->
                    // No feed
                    None

/// Determine the function to retrieve posts for the given feed
let private getFeedPosts (webLog : WebLog) feedType =
    match feedType with
    | StandardFeed       -> Data.Post.findPageOfPublishedPosts webLog.id 1
    | CategoryFeed catId -> Data.Post.findPageOfCategorizedPosts webLog.id [ catId ] 1
    | TagFeed       tag  -> Data.Post.findPageOfTaggedPosts webLog.id tag 1
    | Custom        feed ->
        match feed.source with
        | Category catId -> Data.Post.findPageOfCategorizedPosts webLog.id [ catId ] 1
        | Tag      tag   -> Data.Post.findPageOfTaggedPosts webLog.id tag 1

/// Strip HTML from a string
let private stripHtml text = Regex.Replace (text, "<(.|\n)*?>", "")

/// Create a feed item from the given post    
let private toFeedItem webLog (authors : MetaItem list) (cats : DisplayCategory[]) (tagMaps : TagMap list)
            (post : Post) =
    let plainText =
        match stripHtml post.text with
        | txt when txt.Length < 255 -> txt
        | txt -> $"{txt.Substring (0, 252)}..."
    let item = SyndicationItem (
        Id              = WebLog.absoluteUrl webLog post.permalink,
        Title           = TextSyndicationContent.CreateHtmlContent post.title,
        PublishDate     = DateTimeOffset post.publishedOn.Value,
        LastUpdatedTime = DateTimeOffset post.updatedOn,
        Content         = TextSyndicationContent.CreatePlaintextContent plainText)
    item.AddPermalink (Uri item.Id)
    
    let encoded =
        post.text.Replace("src=\"/", $"src=\"{webLog.urlBase}/").Replace ("href=\"/", $"href=\"{webLog.urlBase}/")
    item.ElementExtensions.Add ("encoded", "http://purl.org/rss/1.0/modules/content/", encoded)
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

/// Add episode information to a podcast feed item
let private addEpisode webLog (feed : CustomFeed) (post : Post) (item : SyndicationItem) =
    let podcast = Option.get feed.podcast
    let meta name = post.metadata |> List.tryFind (fun it -> it.name = name)
    let value (item : MetaItem) = item.value
    let epMediaUrl =
        match (meta >> Option.get >> value) "media" with
        | link when link.StartsWith "http" -> link
        | link -> WebLog.absoluteUrl webLog (Permalink link)
    let epMediaType =
        match meta "mediaType", podcast.defaultMediaType with
        | Some epType, _ -> Some epType.value
        | None, Some defType -> Some defType
        | _ -> None
    let epImageUrl =
        match defaultArg ((meta >> Option.map value) "image") (Permalink.toString podcast.imageUrl) with
        | link when link.StartsWith "http" -> link
        | link -> WebLog.absoluteUrl webLog (Permalink link)
    let epExplicit =
        try
            (meta >> Option.map (value >> ExplicitRating.parse)) "explicit"
            |> Option.defaultValue podcast.explicit
            |> ExplicitRating.toString
        with :? ArgumentException -> ExplicitRating.toString podcast.explicit
    
    let encXml = XmlDocument ()
    let encElt = encXml.CreateElement "enclosure"
    encElt.SetAttribute ("url", epMediaUrl)
    meta "length" |> Option.iter (fun it -> encElt.SetAttribute ("length", it.value))
    epMediaType |> Option.iter (fun typ -> encElt.SetAttribute ("type", typ))
    item.ElementExtensions.Add ("enclosure", "", encXml)
    
    item.ElementExtensions.Add ("creator", "dc", podcast.displayedAuthor)
    item.ElementExtensions.Add ("author", "itunes", podcast.displayedAuthor)
    meta "subtitle" |> Option.iter (fun it -> item.ElementExtensions.Add ("subtitle", "itunes", it.value))
    item.ElementExtensions.Add ("summary", "itunes", stripHtml post.text)
    item.ElementExtensions.Add ("image", "itunes", epImageUrl)
    item.ElementExtensions.Add ("explicit", "itunes", epExplicit)
    meta "duration" |> Option.iter (fun it -> item.ElementExtensions.Add ("duration", "itunes", it.value))
    
    if post.metadata |> List.exists (fun it -> it.name = "chapter") then
        try
            let chapXml = XmlDocument ()
            let chapsElt = chapXml.CreateElement ("psc", "chapters", "")
            chapsElt.SetAttribute ("version", "1.2")
            
            post.metadata
            |> List.filter (fun it -> it.name = "chapter")
            |> List.map (fun it ->
                TimeSpan.Parse (it.value.Split(" ")[0]), it.value.Substring (it.value.IndexOf(" ") + 1))
            |> List.sortBy fst
            |> List.iter (fun chap ->
                let chapElt = chapXml.CreateElement ("psc", "chapter", "")
                chapElt.SetAttribute ("start", (fst chap).ToString "hh:mm:ss")
                chapElt.SetAttribute ("title", snd chap)
                chapsElt.AppendChild chapElt |> ignore)
            
            chapXml.AppendChild chapsElt |> ignore
            item.ElementExtensions.Add ("chapters", "psc", chapXml)
        with _ -> ()
    item
    
/// Add a namespace to the feed
let private addNamespace (feed : SyndicationFeed) alias nsUrl =
    feed.AttributeExtensions.Add (XmlQualifiedName (alias, "http://www.w3.org/2000/xmlns/"), nsUrl)

/// Add items to the top of the feed required for podcasts
let private addPodcast webLog (rssFeed : SyndicationFeed) (feed : CustomFeed) =
    let addChild (doc : XmlDocument) name prefix value =
        let child = doc.CreateElement (name, prefix, "") |> doc.AppendChild
        child.Value <- value
        doc
    
    let podcast  = Option.get feed.podcast
    let feedUrl  = WebLog.absoluteUrl webLog feed.path
    let imageUrl =
        match podcast.imageUrl with
        | Permalink link when link.StartsWith "http" -> link
        | Permalink _ -> WebLog.absoluteUrl webLog podcast.imageUrl
    
    let categoryXml = XmlDocument ()
    let catElt = categoryXml.CreateElement ("itunes", "category", "")
    catElt.SetAttribute ("text", podcast.iTunesCategory)
    let subCat = categoryXml.CreateElement ("itunes", "category", "")
    subCat.SetAttribute ("text", podcast.iTunesSubcategory)
    catElt.AppendChild subCat |> ignore
    categoryXml.AppendChild catElt |> ignore
    
    [ "dc",       "http://purl.org/dc/elements/1.1/"
      "itunes",   "http://www.itunes.com/dtds/podcast-1.0.dtd"
      "psc",      "http://podlove.org/simple-chapters/"
      "rawvoice", "http://www.rawvoice.com/rawvoiceRssModule/"
    ]
    |> List.iter (fun (alias, nsUrl) -> addNamespace rssFeed alias nsUrl)
    
    rssFeed.ElementExtensions.Add
        ("image", "",
            [ "title", podcast.title
              "url",   imageUrl
              "link",  feedUrl
            ]
            |> List.fold (fun doc (name, value) -> addChild doc name "" value) (XmlDocument ()))
    // TODO: is copyright required?
    rssFeed.ElementExtensions.Add ("summary", "itunes", podcast.summary)
    rssFeed.ElementExtensions.Add ("author",  "itunes", podcast.displayedAuthor)
    podcast.subtitle |> Option.iter (fun sub -> rssFeed.ElementExtensions.Add ("subtitle", "itunes", sub))
    rssFeed.ElementExtensions.Add
        ("owner", "itunes",
            [ "name", podcast.displayedAuthor
              "email", podcast.email
            ]
            |> List.fold (fun doc (name, value) -> addChild doc name "itunes" value) (XmlDocument ()))
    rssFeed.ElementExtensions.Add ("image",     "itunes",   imageUrl)
    rssFeed.ElementExtensions.Add ("category",  "itunes",   categoryXml)
    rssFeed.ElementExtensions.Add ("explicit",  "itunes",   ExplicitRating.toString podcast.explicit)
    rssFeed.ElementExtensions.Add ("subscribe", "rawvoice", feedUrl)

/// Create a feed with a known non-zero-length list of posts    
let createFeed (feedType : FeedType) posts : HttpHandler = fun next ctx -> backgroundTask {
    let  webLog  = ctx.WebLog
    let  conn    = ctx.Conn
    let! authors = Post.getAuthors     webLog posts conn
    let! tagMaps = Post.getTagMappings webLog posts conn
    let  cats    = CategoryCache.get ctx
    let  podcast = match feedType with Custom feed when Option.isSome feed.podcast -> Some feed | _ -> None
    
    let toItem post =
        let item = toFeedItem webLog authors cats tagMaps post
        match podcast with
        | Some feed when post.metadata |> List.exists (fun it -> it.name = "media") ->
            addEpisode webLog feed post item
        | _ -> item
        
    let feed  = SyndicationFeed ()
    feed.Title           <- TextSyndicationContent webLog.name
    feed.Description     <- TextSyndicationContent <| defaultArg webLog.subtitle webLog.name
    feed.LastUpdatedTime <- DateTimeOffset <| (List.head posts).updatedOn
    feed.Generator       <- generator ctx
    feed.Items           <- posts |> Seq.ofList |> Seq.map toItem
    feed.Language        <- "en"
    feed.Id              <- webLog.urlBase
    
    feed.Links.Add (SyndicationLink (Uri $"{webLog.urlBase}/feed.xml", "self", "", "application/rss+xml", 0L))
    feed.AttributeExtensions.Add
        (XmlQualifiedName ("content", "http://www.w3.org/2000/xmlns/"), "http://purl.org/rss/1.0/modules/content/")
    feed.ElementExtensions.Add ("link", "", webLog.urlBase)
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
    match! getFeedPosts ctx.WebLog feedType postCount ctx.Conn with
    | posts when List.length posts > 0 -> return! createFeed feedType posts next ctx
    | _ -> return! Error.notFound next ctx
}

// ~~ FEED ADMINISTRATION ~~

open DotLiquid

// GET: /admin/rss/settings
let editSettings : HttpHandler = fun next ctx -> task {
    // TODO: stopped here
    return!
        Hash.FromAnonymousObject
            {|  csrf = csrfToken ctx
            |}
        |> viewForTheme "admin" "rss-settings" next ctx
}
