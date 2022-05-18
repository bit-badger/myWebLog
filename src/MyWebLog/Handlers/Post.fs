/// Handlers to manipulate posts
module MyWebLog.Handlers.Post

open System
open Giraffe
open Microsoft.AspNetCore.Http

/// Split the "rest" capture for categories and tags into the page number and category/tag URL parts
let private pathAndPageNumber (ctx : HttpContext) =
    let slugs     = (string ctx.Request.RouteValues["slug"]).Split "/" |> Array.filter (fun it -> it <> "")
    let pageIdx   = Array.IndexOf (slugs, "page")
    let pageNbr   = if pageIdx > 0 then (int64 slugs[pageIdx + 1]) else 1L
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

open System.Threading.Tasks
open DotLiquid
open MyWebLog.ViewModels

/// Convert a list of posts into items ready to be displayed
let private preparePostList webLog posts listType url pageNbr perPage ctx conn = task {
    let! authors = getAuthors webLog posts conn
    let postItems =
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
        | SinglePost,   _  -> newerPost |> Option.map (fun p -> Permalink.toString p.permalink)
        | _,            1L -> None
        | PostList,     2L    when webLog.defaultPage = "posts" -> Some ""
        | PostList,     _  -> Some $"page/{pageNbr - 1L}"
        | CategoryList, 2L -> Some $"category/{url}/"
        | CategoryList, _  -> Some $"category/{url}/page/{pageNbr - 1L}"
        | TagList,      2L -> Some $"tag/{url}/"
        | TagList,      _  -> Some $"tag/{url}/page/{pageNbr - 1L}"
        | AdminList,    2L -> Some "posts"
        | AdminList,    _  -> Some $"posts/page/{pageNbr - 1L}"
    let olderLink =
        match listType, List.length posts > perPage with
        | SinglePost,   _     -> olderPost |> Option.map (fun p -> Permalink.toString p.permalink)
        | _,            false -> None
        | PostList,     true  -> Some $"page/{pageNbr + 1L}"
        | CategoryList, true  -> Some $"category/{url}/page/{pageNbr + 1L}"
        | TagList,      true  -> Some $"tag/{url}/page/{pageNbr + 1L}"
        | AdminList,    true  -> Some $"posts/page/{pageNbr + 1L}"
    let model =
        { posts      = postItems
          authors    = authors
          subtitle   = None
          newerLink  = newerLink
          newerName  = newerPost |> Option.map (fun p -> p.title)
          olderLink  = olderLink
          olderName  = olderPost |> Option.map (fun p -> p.title)
        }
    return Hash.FromAnonymousObject {| model = model; categories = CategoryCache.get ctx |}
}

// GET /page/{pageNbr}
let pageOfPosts pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = WebLogCache.get ctx
    let  conn   = conn ctx
    let! posts  = Data.Post.findPageOfPublishedPosts webLog.id pageNbr webLog.postsPerPage conn
    let! hash   = preparePostList webLog posts PostList "" pageNbr webLog.postsPerPage ctx conn
    let  title  =
        match pageNbr, webLog.defaultPage with
        | 1L, "posts" -> None
        | _,  "posts" -> Some $"Page {pageNbr}"
        | _,  _       -> Some $"Page {pageNbr} &laquo; Posts"
    match title with Some ttl -> hash.Add ("page_title", ttl) | None -> ()
    if pageNbr = 1L && webLog.defaultPage = "posts" then hash.Add ("is_home", true)
    return! themedView "index" next ctx hash
}

// GET /category/{slug}/
// GET /category/{slug}/page/{pageNbr}
let pageOfCategorizedPosts : HttpHandler = fun next ctx -> task {
    let  webLog  = WebLogCache.get ctx
    let  conn    = conn ctx
    let  pageNbr, slug = pathAndPageNumber ctx
    let  allCats = CategoryCache.get ctx
    let  cat     = allCats |> Array.find (fun cat -> cat.slug = slug)
    // Category pages include posts in subcategories
    let  catIds  =
        allCats
        |> Seq.ofArray
        |> Seq.filter (fun c -> c.id = cat.id || Array.contains cat.name c.parentNames)
        |> Seq.map (fun c -> CategoryId c.id)
        |> List.ofSeq
    match! Data.Post.findPageOfCategorizedPosts webLog.id catIds pageNbr webLog.postsPerPage conn with
    | posts when List.length posts > 0 ->
        let! hash    = preparePostList webLog posts CategoryList cat.slug pageNbr webLog.postsPerPage ctx conn
        let  pgTitle = if pageNbr = 1L then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
        hash.Add ("page_title", $"{cat.name}: Category Archive{pgTitle}")
        hash.Add ("subtitle", cat.description.Value)
        hash.Add ("is_category", true)
        return! themedView "index" next ctx hash
    | _ -> return! Error.notFound next ctx
}

open System.Web

// GET /tag/{tag}/
// GET /tag/{tag}/page/{pageNbr}
let pageOfTaggedPosts : HttpHandler = fun next ctx -> task {
    let  webLog  = WebLogCache.get ctx
    let  conn    = conn ctx
    let  pageNbr, rawTag = pathAndPageNumber ctx
    let  tag     = HttpUtility.UrlDecode rawTag
    match! Data.Post.findPageOfTaggedPosts webLog.id tag pageNbr webLog.postsPerPage conn with
    | posts when List.length posts > 0 ->
        let! hash    = preparePostList webLog posts TagList rawTag pageNbr webLog.postsPerPage ctx conn
        let  pgTitle = if pageNbr = 1L then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
        hash.Add ("page_title", $"Posts Tagged &ldquo;{tag}&rdquo;{pgTitle}")
        hash.Add ("is_tag", true)
        return! themedView "index" next ctx hash
    // Other systems use hyphens for spaces; redirect if this is an old tag link
    | _ ->
        let spacedTag = tag.Replace ("-", " ")
        match! Data.Post.findPageOfTaggedPosts webLog.id spacedTag pageNbr 1 conn with
        | posts when List.length posts > 0 ->
            let endUrl = if pageNbr = 1L then "" else $"page/{pageNbr}"
            return! redirectTo true $"""/tag/{spacedTag.Replace (" ", "+")}/{endUrl}""" next ctx
        | _ -> return! Error.notFound next ctx
}

// GET /
let home : HttpHandler = fun next ctx -> task {
    let webLog = WebLogCache.get ctx
    match webLog.defaultPage with
    | "posts" -> return! pageOfPosts 1 next ctx
    | pageId ->
        match! Data.Page.findById (PageId pageId) webLog.id (conn ctx) with
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

open System.IO
open System.ServiceModel.Syndication
open System.Text.RegularExpressions
open System.Xml

// GET /feed.xml
//   (Routing handled by catch-all handler for future configurability)
let generateFeed : HttpHandler = fun next ctx -> backgroundTask {
    let  conn    = conn ctx
    let  webLog  = WebLogCache.get ctx
    let  urlBase = $"https://{webLog.urlBase}/"
    // TODO: hard-coded number of items
    let! posts   = Data.Post.findPageOfPublishedPosts webLog.id 1L 10 conn
    let! authors = getAuthors webLog posts conn
    let  cats    = CategoryCache.get ctx
    
    let toItem (post : Post) =
        let plainText =
            Regex.Replace (post.text, "<(.|\n)*?>", "")
            |> function
            | txt when txt.Length < 255 -> txt
            | txt -> $"{txt.Substring (0, 252)}..."
        let item = SyndicationItem (
            Id              = $"{urlBase}{Permalink.toString post.permalink}",
            Title           = TextSyndicationContent.CreateHtmlContent post.title,
            PublishDate     = DateTimeOffset post.publishedOn.Value,
            LastUpdatedTime = DateTimeOffset post.updatedOn,
            Content         = TextSyndicationContent.CreatePlaintextContent plainText)
        item.AddPermalink (Uri item.Id)
        
        let encoded = post.text.Replace("src=\"/", $"src=\"{urlBase}").Replace ("href=\"/", $"href=\"{urlBase}")
        item.ElementExtensions.Add ("encoded", "http://purl.org/rss/1.0/modules/content/", encoded)
        item.Authors.Add (SyndicationPerson (
            Name = (authors |> List.find (fun a -> a.name = WebLogUserId.toString post.authorId)).value))
        [ post.categoryIds
          |> List.map (fun catId ->
              let cat = cats |> Array.find (fun c -> c.id = CategoryId.toString catId)
              SyndicationCategory (cat.name, $"{urlBase}category/{cat.slug}/", cat.name))
          post.tags
          |> List.map (fun tag ->
              let urlTag = tag.Replace (" ", "+")
              SyndicationCategory (tag, $"{urlBase}tag/{urlTag}/", $"{tag} (tag)"))
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
    feed.Id              <- urlBase
    
    feed.Links.Add (SyndicationLink (Uri $"{urlBase}feed.xml", "self", "", "application/rss+xml", 0L))
    feed.AttributeExtensions.Add
        (XmlQualifiedName ("content", "http://www.w3.org/2000/xmlns/"), "http://purl.org/rss/1.0/modules/content/")
    feed.ElementExtensions.Add ("link", "", urlBase)
    
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
let private deriveAction ctx : HttpHandler seq =
    let webLog    = WebLogCache.get ctx
    let conn      = conn ctx
    let permalink = (string >> Permalink) ctx.Request.RouteValues["link"]
    let await it  = (Async.AwaitTask >> Async.RunSynchronously) it
    seq {
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
        // TODO: configure this via web log
        if Permalink.toString permalink = "feed.xml" then yield generateFeed
        // Prior post
        match Data.Post.findCurrentPermalink permalink webLog.id conn |> await with
        | Some link -> yield redirectTo true $"/{Permalink.toString link}"
        | None -> ()
        // Prior permalink
        match Data.Page.findCurrentPermalink permalink webLog.id conn |> await with
        | Some link -> yield redirectTo true $"/{Permalink.toString link}"
        | None -> ()
    }

// GET {**link}
let catchAll : HttpHandler = fun next ctx -> task {
    match deriveAction ctx |> Seq.tryHead with
    | Some handler -> return! handler next ctx
    | None -> return! Error.notFound next ctx
}

// GET /posts
// GET /posts/page/{pageNbr}
let all pageNbr : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLog = WebLogCache.get ctx
    let  conn   = conn ctx
    let! posts  = Data.Post.findPageOfPosts webLog.id pageNbr 25 conn
    let! hash   = preparePostList webLog posts AdminList "" pageNbr 25 ctx conn
    hash.Add ("page_title", "Posts")
    return! viewForTheme "admin" "post-list" next ctx hash
}

// GET /post/{id}/edit
let edit postId : HttpHandler = requireUser >=> fun next ctx -> task {
    let  webLog = WebLogCache.get ctx
    let  conn   = conn     ctx
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

// POST /post/save
let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
    let! model    = ctx.BindFormAsync<EditPostModel> ()
    let  webLogId = webLogId ctx
    let  conn     = conn     ctx
    let  now      = DateTime.UtcNow
    let! pst      = task {
        match model.postId with
        | "new" ->
            return Some
                { Post.empty with
                    id        = PostId.create ()
                    webLogId  = webLogId
                    authorId  = userId ctx
                }
        | postId -> return! Data.Post.findByFullId (PostId postId) webLogId conn
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
        do! (match model.postId with "new" -> Data.Post.add | _ -> Data.Post.update) post conn
        // If the post was published or its categories changed, refresh the category cache
        if model.doPublish
           || not (pst.Value.categoryIds
                   |> List.append post.categoryIds
                   |> List.distinct
                   |> List.length = List.length pst.Value.categoryIds) then
            do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Post saved successfully" }
        return! redirectToGet $"/post/{PostId.toString post.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}
