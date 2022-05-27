/// Handlers to manipulate posts
module MyWebLog.Handlers.Post

open System
open Microsoft.AspNetCore.Http

/// Parse a slug and page number from an "everything else" URL
let private parseSlugAndPage (slugAndPage : string seq) =
    let slugs   = (slugAndPage |> Seq.skip 1 |> Seq.head).Split "/" |> Array.filter (fun it -> it <> "")
    let pageIdx = Array.IndexOf (slugs, "page")
    let pageNbr =
        match pageIdx with
        | -1 -> Some 1
        | idx when idx + 2 = slugs.Length -> Some (int slugs[pageIdx + 1])
        | _ -> None
    let slugParts = if pageIdx > 0 then Array.truncate pageIdx slugs else slugs
    pageNbr, String.Join ("/", slugParts)

/// The type of post list being prepared
type ListType =
    | AdminList
    | CategoryList
    | PostList
    | SinglePost
    | TagList

open MyWebLog

/// Get all authors for a list of posts as metadata items
let private getAuthors (webLog : WebLog) (posts : Post list) conn =
    posts
    |> List.map (fun p -> p.authorId)
    |> List.distinct
    |> Data.WebLogUser.findNames webLog.id conn

/// Get all tag mappings for a list of posts as metadata items
let private getTagMappings (webLog : WebLog) (posts : Post list) =
    posts
    |> List.map (fun p -> p.tags)
    |> List.concat
    |> List.distinct
    |> fun tags -> Data.TagMap.findMappingForTags tags webLog.id
    
open System.Threading.Tasks
open DotLiquid
open MyWebLog.ViewModels

/// Convert a list of posts into items ready to be displayed
let private preparePostList webLog posts listType (url : string) pageNbr perPage ctx conn = task {
    let! authors     = getAuthors     webLog posts conn
    let! tagMappings = getTagMappings webLog posts conn
    let  relUrl it   = Some <| WebLog.relativeUrl webLog (Permalink it)
    let  postItems   =
        posts
        |> Seq.ofList
        |> Seq.truncate perPage
        |> Seq.map (PostListItem.fromPost webLog)
        |> Array.ofSeq
    let! olderPost, newerPost =
        match listType with
        | SinglePost ->
            let post     = List.head posts
            let dateTime = defaultArg post.publishedOn post.updatedOn
            Data.Post.findSurroundingPosts webLog.id dateTime conn
        | _ -> Task.FromResult (None, None)
    let newerLink =
        match listType, pageNbr with
        | SinglePost,   _ -> newerPost |> Option.map (fun p -> Permalink.toString p.permalink)
        | _,            1 -> None
        | PostList,     2    when webLog.defaultPage = "posts" -> Some ""
        | PostList,     _ -> relUrl $"page/{pageNbr - 1}"
        | CategoryList, 2 -> relUrl $"category/{url}/"
        | CategoryList, _ -> relUrl $"category/{url}/page/{pageNbr - 1}"
        | TagList,      2 -> relUrl $"tag/{url}/"
        | TagList,      _ -> relUrl $"tag/{url}/page/{pageNbr - 1}"
        | AdminList,    2 -> relUrl "admin/posts"
        | AdminList,    _ -> relUrl $"admin/posts/page/{pageNbr - 1}"
    let olderLink =
        match listType, List.length posts > perPage with
        | SinglePost,   _     -> olderPost |> Option.map (fun p -> Permalink.toString p.permalink)
        | _,            false -> None
        | PostList,     true  -> relUrl $"page/{pageNbr + 1}"
        | CategoryList, true  -> relUrl $"category/{url}/page/{pageNbr + 1}"
        | TagList,      true  -> relUrl $"tag/{url}/page/{pageNbr + 1}"
        | AdminList,    true  -> relUrl $"admin/posts/page/{pageNbr + 1}"
    let model =
        { posts      = postItems
          authors    = authors
          subtitle   = None
          newerLink  = newerLink
          newerName  = newerPost |> Option.map (fun p -> p.title)
          olderLink  = olderLink
          olderName  = olderPost |> Option.map (fun p -> p.title)
        }
    return Hash.FromAnonymousObject {| model = model; categories = CategoryCache.get ctx; tag_mappings = tagMappings |}
}

open Giraffe

// GET /page/{pageNbr}
let pageOfPosts pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let! posts  = Data.Post.findPageOfPublishedPosts webLog.id pageNbr webLog.postsPerPage conn
    let! hash   = preparePostList webLog posts PostList "" pageNbr webLog.postsPerPage ctx conn
    let  title  =
        match pageNbr, webLog.defaultPage with
        | 1, "posts" -> None
        | _, "posts" -> Some $"Page {pageNbr}"
        | _,  _      -> Some $"Page {pageNbr} &laquo; Posts"
    match title with Some ttl -> hash.Add ("page_title", ttl) | None -> ()
    if pageNbr = 1 && webLog.defaultPage = "posts" then hash.Add ("is_home", true)
    return! themedView "index" next ctx hash
}

// GET /category/{slug}/
// GET /category/{slug}/page/{pageNbr}
let pageOfCategorizedPosts slugAndPage : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let conn   = ctx.Conn
    match parseSlugAndPage slugAndPage with
    | Some pageNbr, slug -> 
        let allCats = CategoryCache.get ctx
        let cat     = allCats |> Array.find (fun cat -> cat.slug = slug)
        // Category pages include posts in subcategories
        let catIds  =
            allCats
            |> Seq.ofArray
            |> Seq.filter (fun c -> c.id = cat.id || Array.contains cat.name c.parentNames)
            |> Seq.map (fun c -> CategoryId c.id)
            |> List.ofSeq
        match! Data.Post.findPageOfCategorizedPosts webLog.id catIds pageNbr webLog.postsPerPage conn with
        | posts when List.length posts > 0 ->
            let! hash    = preparePostList webLog posts CategoryList cat.slug pageNbr webLog.postsPerPage ctx conn
            let  pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
            hash.Add ("page_title", $"{cat.name}: Category Archive{pgTitle}")
            hash.Add ("subtitle", cat.description.Value)
            hash.Add ("is_category", true)
            return! themedView "index" next ctx hash
        | _ -> return! Error.notFound next ctx
    | None, _ -> return! Error.notFound next ctx
}

open System.Web

// GET /tag/{tag}/
// GET /tag/{tag}/page/{pageNbr}
let pageOfTaggedPosts slugAndPage : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let conn   = ctx.Conn
    match parseSlugAndPage slugAndPage with
    | Some pageNbr, rawTag -> 
        let  urlTag = HttpUtility.UrlDecode rawTag
        let! tag    = backgroundTask {
            match! Data.TagMap.findByUrlValue urlTag webLog.id conn with
            | Some m -> return m.tag
            | None   -> return urlTag
        }
        match! Data.Post.findPageOfTaggedPosts webLog.id tag pageNbr webLog.postsPerPage conn with
        | posts when List.length posts > 0 ->
            let! hash    = preparePostList webLog posts TagList rawTag pageNbr webLog.postsPerPage ctx conn
            let  pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
            hash.Add ("page_title", $"Posts Tagged &ldquo;{tag}&rdquo;{pgTitle}")
            hash.Add ("is_tag", true)
            return! themedView "index" next ctx hash
        // Other systems use hyphens for spaces; redirect if this is an old tag link
        | _ ->
            let spacedTag = tag.Replace ("-", " ")
            match! Data.Post.findPageOfTaggedPosts webLog.id spacedTag pageNbr 1 conn with
            | posts when List.length posts > 0 ->
                let endUrl = if pageNbr = 1 then "" else $"page/{pageNbr}"
                return!
                    redirectTo true
                        (WebLog.relativeUrl webLog (Permalink $"""tag/{spacedTag.Replace (" ", "+")}/{endUrl}"""))
                        next ctx
            | _ -> return! Error.notFound next ctx
    | None, _ -> return! Error.notFound next ctx
}

// GET /
let home : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match webLog.defaultPage with
    | "posts" -> return! pageOfPosts 1 next ctx
    | pageId ->
        match! Data.Page.findById (PageId pageId) webLog.id ctx.Conn with
        | Some page ->
            return!
                Hash.FromAnonymousObject {|
                    page       = DisplayPage.fromPage webLog page
                    page_title = page.title
                    is_home    = true
                |}
                |> themedView (defaultArg page.template "single-page") next ctx
        | None -> return! Error.notFound next ctx
}

/// Functions to support generating RSS feeds
module Feed =
    
    open System.IO
    open System.ServiceModel.Syndication
    open System.Text.RegularExpressions
    open System.Xml

    /// The type of feed to generate
    type FeedType =
        | Standard
        | Category of CategoryId
        | Tag      of string
        | Custom   of CustomFeed
    
    /// Derive the type of RSS feed requested
    let deriveFeedType ctx webLog feedPath : (FeedType * int) option =
        let name = $"/{webLog.rss.feedName}"
        let postCount = defaultArg webLog.rss.itemsInFeed webLog.postsPerPage
        // Standard feed
        match webLog.rss.feedEnabled && feedPath = name with
        | true -> Some (Standard, postCount)
        | false ->
            // Category feed
            match CategoryCache.get ctx |> Array.tryFind (fun cat -> cat.slug = feedPath.Replace (name, "")) with
            | Some cat -> Some (Category (CategoryId cat.id), postCount)
            | None ->
                // Tag feed
                match feedPath.StartsWith "/tag/" with
                | true -> Some (Tag (feedPath.Replace("/tag/", "").Replace(name, "")), postCount)
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
        
    // GET {any-prescribed-feed}
    let generate (feedType : FeedType) postCount : HttpHandler = fun next ctx -> backgroundTask {
        // TODO: stopped here; use feed type and count in the below function
        let  webLog  = ctx.WebLog
        let  conn    = ctx.Conn
        let! posts   = Data.Post.findPageOfPublishedPosts webLog.id 1 postCount conn
        let! authors = getAuthors     webLog posts conn
        let! tagMaps = getTagMappings webLog posts conn
        let  cats    = CategoryCache.get ctx
        
        let toItem (post : Post) =
            let plainText =
                Regex.Replace (post.text, "<(.|\n)*?>", "")
                |> function
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
            
        
        let feed = SyndicationFeed ()
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
        
        use mem = new MemoryStream ()
        use xml = XmlWriter.Create mem
        feed.SaveAsRss20 xml
        xml.Close ()
        
        let _ = mem.Seek (0L, SeekOrigin.Begin)
        let rdr = new StreamReader(mem)
        let! output = rdr.ReadToEndAsync ()
        
        return! ( setHttpHeader "Content-Type" "text/xml" >=> setStatusCode 200 >=> setBodyFromString output) next ctx
    }


/// Sequence where the first returned value is the proper handler for the link
let private deriveAction (ctx : HttpContext) : HttpHandler seq =
    let webLog   = ctx.WebLog
    let conn     = ctx.Conn
    let textLink =
        let _, extra = WebLog.hostAndPath webLog
        let url      = string ctx.Request.Path
        (if extra = "" then url else url.Substring extra.Length).ToLowerInvariant ()
    let await it = (Async.AwaitTask >> Async.RunSynchronously) it
    seq {
        debug "Post" ctx (fun () -> $"Considering URL {textLink}")
        // Home page directory without the directory slash 
        if textLink = "" then yield redirectTo true (WebLog.relativeUrl webLog Permalink.empty)
        let permalink = Permalink (textLink.Substring 1)
        // Current post
        match Data.Post.findByPermalink permalink webLog.id conn |> await with
        | Some post ->
            let model = preparePostList webLog [ post ] SinglePost "" 1 1 ctx conn |> await
            model.Add ("page_title", post.title)
            yield fun next ctx -> themedView "single-post" next ctx model
        | None -> ()
        // Current page
        match Data.Page.findByPermalink permalink webLog.id conn |> await with
        | Some page ->
            yield fun next ctx ->
                Hash.FromAnonymousObject {| page = DisplayPage.fromPage webLog page; page_title = page.title |}
                |> themedView (defaultArg page.template "single-page") next ctx
        | None -> ()
        // RSS feed
        match Feed.deriveFeedType ctx webLog textLink with
        | Some (feedType, postCount) -> yield Feed.generate feedType postCount 
        | None -> ()
        // Post differing only by trailing slash
        let altLink = Permalink (if textLink.EndsWith "/" then textLink[..textLink.Length - 2] else $"{textLink}/")
        match Data.Post.findByPermalink altLink webLog.id conn |> await with
        | Some post -> yield redirectTo true (WebLog.relativeUrl webLog post.permalink)
        | None -> ()
        // Page differing only by trailing slash
        match Data.Page.findByPermalink altLink webLog.id conn |> await with
        | Some page -> yield redirectTo true (WebLog.relativeUrl webLog page.permalink)
        | None -> ()
        // Prior post
        match Data.Post.findCurrentPermalink [ permalink; altLink ] webLog.id conn |> await with
        | Some link -> yield redirectTo true (WebLog.relativeUrl webLog link)
        | None -> ()
        // Prior page
        match Data.Page.findCurrentPermalink [ permalink; altLink ] webLog.id conn |> await with
        | Some link -> yield redirectTo true (WebLog.relativeUrl webLog link)
        | None -> ()
    }

// GET {all-of-the-above}
let catchAll : HttpHandler = fun next ctx -> task {
    match deriveAction ctx |> Seq.tryHead with
    | Some handler -> return! handler next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/posts
// GET /admin/posts/page/{pageNbr}
let all pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let! posts  = Data.Post.findPageOfPosts webLog.id pageNbr 25 conn
    let! hash   = preparePostList webLog posts AdminList "" pageNbr 25 ctx conn
    hash.Add ("page_title", "Posts")
    hash.Add ("csrf", csrfToken ctx)
    return! viewForTheme "admin" "post-list" next ctx hash
}

// GET /admin/post/{id}/edit
let edit postId : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let! result = task {
        match postId with
        | "new" -> return Some ("Write a New Post", { Post.empty with id = PostId "new" })
        | _ ->
            match! Data.Post.findByFullId (PostId postId) webLog.id conn with
            | Some post -> return Some ("Edit Post", post)
            | None -> return None
    }
    match result with
    | Some (title, post) ->
        let! cats = Data.Category.findAllForView webLog.id conn
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = EditPostModel.fromPost webLog post
                page_title = title
                categories = cats
            |}
            |> viewForTheme "admin" "post-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/permalinks
let editPermalinks postId : HttpHandler = fun next ctx -> task {
    match! Data.Post.findByFullId (PostId postId) ctx.WebLog.id ctx.Conn with
    | Some post ->
        return!
            Hash.FromAnonymousObject {|
                csrf       = csrfToken ctx
                model      = ManagePermalinksModel.fromPost post
                page_title = $"Manage Prior Permalinks"
            |}
            |> viewForTheme "admin" "permalinks" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/post/permalinks
let savePermalinks : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! model  = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  links  = model.prior |> Array.map Permalink |> List.ofArray
    match! Data.Post.updatePriorPermalinks (PostId model.id) webLog.id links ctx.Conn with
    | true ->
        do! addMessage ctx { UserMessage.success with message = "Post permalinks saved successfully" }
        return! redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/post/{model.id}/permalinks")) next ctx
    | false -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/delete
let delete postId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! Data.Post.delete (PostId postId) webLog.id ctx.Conn with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Post deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Post not found; nothing deleted" }
    return! redirectToGet (WebLog.relativeUrl webLog (Permalink "admin/posts")) next ctx
}

#nowarn "3511"

// POST /admin/post/save
let save : HttpHandler = fun next ctx -> task {
    let! model  = ctx.BindFormAsync<EditPostModel> ()
    let  webLog = ctx.WebLog
    let  conn   = ctx.Conn
    let  now    = DateTime.UtcNow
    let! pst    = task {
        match model.postId with
        | "new" ->
            return Some
                { Post.empty with
                    id        = PostId.create ()
                    webLogId  = webLog.id
                    authorId  = userId ctx
                }
        | postId -> return! Data.Post.findByFullId (PostId postId) webLog.id conn
    }
    match pst with
    | Some post ->
        let revision = { asOf = now; text = MarkupText.parse $"{model.source}: {model.text}" }
        // Detect a permalink change, and add the prior one to the prior list
        let post =
            match Permalink.toString post.permalink with
            | "" -> post
            | link when link = model.permalink -> post
            | _ -> { post with priorPermalinks = post.permalink :: post.priorPermalinks }
        let post =
            { post with
                title       = model.title
                permalink   = Permalink model.permalink
                publishedOn = if model.doPublish then Some now else post.publishedOn
                updatedOn   = now
                text        = MarkupText.toHtml revision.text
                tags        = model.tags.Split ","
                              |> Seq.ofArray
                              |> Seq.map (fun it -> it.Trim().ToLower ())
                              |> Seq.filter (fun it -> it <> "")
                              |> Seq.sort
                              |> List.ofSeq
                categoryIds = model.categoryIds |> Array.map CategoryId |> List.ofArray
                status      = if model.doPublish then Published else post.status
                metadata    = Seq.zip model.metaNames model.metaValues
                              |> Seq.filter (fun it -> fst it > "")
                              |> Seq.map (fun it -> { name = fst it; value = snd it })
                              |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                              |> List.ofSeq
                revisions   = match post.revisions |> List.tryHead with
                              | Some r when r.text = revision.text -> post.revisions
                              | _ -> revision :: post.revisions
            }
        let post =
            match model.setPublished with
            | true ->
                let dt = DateTime (model.pubOverride.Value.ToUniversalTime().Ticks, DateTimeKind.Utc)
                printf $"**** DateKind = {dt.Kind}"
                match model.setUpdated with
                | true ->
                    { post with
                        publishedOn = Some dt
                        updatedOn   = dt
                        revisions   = [ { (List.head post.revisions) with asOf = dt } ]
                    }
                | false -> { post with publishedOn = Some dt }
            | false -> post
        do! (if model.postId = "new" then Data.Post.add else Data.Post.update) post conn
        // If the post was published or its categories changed, refresh the category cache
        if model.doPublish
           || not (pst.Value.categoryIds
                   |> List.append post.categoryIds
                   |> List.distinct
                   |> List.length = List.length pst.Value.categoryIds) then
            do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Post saved successfully" }
        return!
            redirectToGet (WebLog.relativeUrl webLog (Permalink $"admin/post/{PostId.toString post.id}/edit")) next ctx
    | None -> return! Error.notFound next ctx
}
