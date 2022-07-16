/// Handlers to manipulate posts
module MyWebLog.Handlers.Post

open System
open System.Collections.Generic
open MyWebLog

/// Parse a slug and page number from an "everything else" URL
let private parseSlugAndPage webLog (slugAndPage : string seq) =
    let fullPath = slugAndPage |> Seq.head
    let slugPath = slugAndPage |> Seq.skip 1 |> Seq.head
    let slugs, isFeed =
        let feedName = $"/{webLog.rss.feedName}"
        let notBlank = Array.filter (fun it -> it <> "")
        if (   (webLog.rss.categoryEnabled && fullPath.StartsWith "/category/")
            || (webLog.rss.tagEnabled      && fullPath.StartsWith "/tag/"     ))
           && slugPath.EndsWith feedName then
            notBlank (slugPath.Replace(feedName, "").Split "/"), true
        else
            notBlank (slugPath.Split "/"), false
    let pageIdx = Array.IndexOf (slugs, "page")
    let pageNbr =
        match pageIdx with
        | -1 -> Some 1
        | idx when idx + 2 = slugs.Length -> Some (int slugs[pageIdx + 1])
        | _ -> None
    let slugParts = if pageIdx > 0 then Array.truncate pageIdx slugs else slugs
    pageNbr, String.Join ("/", slugParts), isFeed

/// The type of post list being prepared
type ListType =
    | AdminList
    | CategoryList
    | PostList
    | SinglePost
    | TagList

open System.Threading.Tasks
open DotLiquid
open MyWebLog.Data
open MyWebLog.ViewModels

/// Convert a list of posts into items ready to be displayed
let preparePostList webLog posts listType (url : string) pageNbr perPage ctx (data : IData) = task {
    let! authors     = getAuthors     webLog posts data
    let! tagMappings = getTagMappings webLog posts data
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
            data.Post.findSurroundingPosts webLog.id dateTime
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
    return Hash.FromAnonymousObject {|
        model        = model
        categories   = CategoryCache.get ctx
        tag_mappings = tagMappings
        is_post      = match listType with SinglePost -> true | _ -> false
    |}
}

open Giraffe

// GET /page/{pageNbr}
let pageOfPosts pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let! posts  = data.Post.findPageOfPublishedPosts webLog.id pageNbr webLog.postsPerPage
    let! hash   = preparePostList webLog posts PostList "" pageNbr webLog.postsPerPage ctx data
    let  title  =
        match pageNbr, webLog.defaultPage with
        | 1, "posts" -> None
        | _, "posts" -> Some $"Page {pageNbr}"
        | _,  _      -> Some $"Page {pageNbr} &laquo; Posts"
    match title with Some ttl -> hash.Add ("page_title", ttl) | None -> ()
    if pageNbr = 1 && webLog.defaultPage = "posts" then hash.Add ("is_home", true)
    return! themedView "index" next ctx hash
}

// GET /page/{pageNbr}/
let redirectToPageOfPosts (pageNbr : int) : HttpHandler = fun next ctx ->
    redirectTo true (WebLog.relativeUrl ctx.WebLog (Permalink $"page/{pageNbr}")) next ctx

// GET /category/{slug}/
// GET /category/{slug}/page/{pageNbr}
let pageOfCategorizedPosts slugAndPage : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let data   = ctx.Data
    match parseSlugAndPage webLog slugAndPage with
    | Some pageNbr, slug, isFeed ->
        match CategoryCache.get ctx |> Array.tryFind (fun cat -> cat.slug = slug) with
        | Some cat when isFeed ->
            return! Feed.generate (Feed.CategoryFeed ((CategoryId cat.id), $"category/{slug}/{webLog.rss.feedName}"))
                        (defaultArg webLog.rss.itemsInFeed webLog.postsPerPage) next ctx
        | Some cat ->
            // Category pages include posts in subcategories
            match! data.Post.findPageOfCategorizedPosts webLog.id (getCategoryIds slug ctx) pageNbr webLog.postsPerPage
                with
            | posts when List.length posts > 0 ->
                let! hash = preparePostList webLog posts CategoryList cat.slug pageNbr webLog.postsPerPage ctx data
                let pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
                hash.Add ("page_title", $"{cat.name}: Category Archive{pgTitle}")
                hash.Add ("subtitle", defaultArg cat.description "")
                hash.Add ("is_category", true)
                hash.Add ("is_category_home", (pageNbr = 1))
                hash.Add ("slug", slug)
                return! themedView "index" next ctx hash
            | _ -> return! Error.notFound next ctx
        | None -> return! Error.notFound next ctx
    | None, _, _ -> return! Error.notFound next ctx
}

open System.Web

// GET /tag/{tag}/
// GET /tag/{tag}/page/{pageNbr}
let pageOfTaggedPosts slugAndPage : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let data   = ctx.Data
    match parseSlugAndPage webLog slugAndPage with
    | Some pageNbr, rawTag, isFeed -> 
        let  urlTag = HttpUtility.UrlDecode rawTag
        let! tag    = backgroundTask {
            match! data.TagMap.findByUrlValue urlTag webLog.id with
            | Some m -> return m.tag
            | None   -> return urlTag
        }
        if isFeed then
            return! Feed.generate (Feed.TagFeed (tag, $"tag/{rawTag}/{webLog.rss.feedName}"))
                        (defaultArg webLog.rss.itemsInFeed webLog.postsPerPage) next ctx
        else
            match! data.Post.findPageOfTaggedPosts webLog.id tag pageNbr webLog.postsPerPage with
            | posts when List.length posts > 0 ->
                let! hash    = preparePostList webLog posts TagList rawTag pageNbr webLog.postsPerPage ctx data
                let  pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
                hash.Add ("page_title", $"Posts Tagged &ldquo;{tag}&rdquo;{pgTitle}")
                hash.Add ("is_tag", true)
                hash.Add ("is_tag_home", (pageNbr = 1))
                hash.Add ("slug", rawTag)
                return! themedView "index" next ctx hash
            // Other systems use hyphens for spaces; redirect if this is an old tag link
            | _ ->
                let spacedTag = tag.Replace ("-", " ")
                match! data.Post.findPageOfTaggedPosts webLog.id spacedTag pageNbr 1 with
                | posts when List.length posts > 0 ->
                    let endUrl = if pageNbr = 1 then "" else $"page/{pageNbr}"
                    return!
                        redirectTo true
                            (WebLog.relativeUrl webLog (Permalink $"""tag/{spacedTag.Replace (" ", "+")}/{endUrl}"""))
                            next ctx
                | _ -> return! Error.notFound next ctx
    | None, _, _ -> return! Error.notFound next ctx
}

// GET /
let home : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match webLog.defaultPage with
    | "posts" -> return! pageOfPosts 1 next ctx
    | pageId ->
        match! ctx.Data.Page.findById (PageId pageId) webLog.id with
        | Some page ->
            return!
                Hash.FromAnonymousObject {|
                    page_title = page.title
                    page       = DisplayPage.fromPage webLog page
                    categories = CategoryCache.get ctx
                    is_home    = true
                |}
                |> themedView (defaultArg page.template "single-page") next ctx
        | None -> return! Error.notFound next ctx
}

// GET /admin/posts
// GET /admin/posts/page/{pageNbr}
let all pageNbr : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let! posts  = data.Post.findPageOfPosts webLog.id pageNbr 25
    let! hash   = preparePostList webLog posts AdminList "" pageNbr 25 ctx data
    hash.Add ("page_title", "Posts")
    hash.Add ("csrf", ctx.CsrfTokenSet)
    return! viewForTheme "admin" "post-list" next ctx hash
}

// GET /admin/post/{id}/edit
let edit postId : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let! result = task {
        match postId with
        | "new" -> return Some ("Write a New Post", { Post.empty with id = PostId "new" })
        | _ ->
            match! data.Post.findFullById (PostId postId) webLog.id with
            | Some post -> return Some ("Edit Post", post)
            | None -> return None
    }
    match result with
    | Some (title, post) ->
        let! cats      = data.Category.findAllForView webLog.id
        let! templates = templatesForTheme ctx "post"
        let  model     = EditPostModel.fromPost webLog post
        return!
            Hash.FromAnonymousObject {|
                page_title      = title
                csrf            = ctx.CsrfTokenSet
                model           = model
                metadata        = Array.zip model.metaNames model.metaValues
                                  |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |])
                templates       = templates
                categories      = cats
                explicit_values = [|
                    KeyValuePair.Create ("", "&ndash; Default &ndash;")
                    KeyValuePair.Create (ExplicitRating.toString Yes,   "Yes")
                    KeyValuePair.Create (ExplicitRating.toString No,    "No")
                    KeyValuePair.Create (ExplicitRating.toString Clean, "Clean")
                |]
            |}
            |> viewForTheme "admin" "post-edit" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/delete
let delete postId : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match! ctx.Data.Post.delete (PostId postId) webLog.id with
    | true  -> do! addMessage ctx { UserMessage.success with message = "Post deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.error with message = "Post not found; nothing deleted" }
    return! redirectToGet "admin/posts" next ctx
}

// GET /admin/post/{id}/permalinks
let editPermalinks postId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.Post.findFullById (PostId postId) ctx.WebLog.id with
    | Some post ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Prior Permalinks"
                csrf       = ctx.CsrfTokenSet
                model      = ManagePermalinksModel.fromPost post
            |}
            |> viewForTheme "admin" "permalinks" next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/post/permalinks
let savePermalinks : HttpHandler = fun next ctx -> task {
    let  webLog = ctx.WebLog
    let! model  = ctx.BindFormAsync<ManagePermalinksModel> ()
    let  links  = model.prior |> Array.map Permalink |> List.ofArray
    match! ctx.Data.Post.updatePriorPermalinks (PostId model.id) webLog.id links with
    | true ->
        do! addMessage ctx { UserMessage.success with message = "Post permalinks saved successfully" }
        return! redirectToGet $"admin/post/{model.id}/permalinks" next ctx
    | false -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/revisions
let editRevisions postId : HttpHandler = fun next ctx -> task {
    match! ctx.Data.Post.findFullById (PostId postId) ctx.WebLog.id with
    | Some post ->
        return!
            Hash.FromAnonymousObject {|
                page_title = "Manage Post Revisions"
                csrf       = ctx.CsrfTokenSet
                model      = ManageRevisionsModel.fromPost ctx.WebLog post
            |}
            |> viewForTheme "admin" "revisions" next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/revisions/purge
let purgeRevisions postId : HttpHandler = fun next ctx -> task {
    let data = ctx.Data
    match! data.Post.findFullById (PostId postId) ctx.WebLog.id with
    | Some post ->
        do! data.Post.update { post with revisions = [ List.head post.revisions ] }
        do! addMessage ctx { UserMessage.success with message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/post/{postId}/revisions" next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the post and the requested revision
let private findPostRevision postId revDate (ctx : HttpContext) = task {
    match! ctx.Data.Post.findFullById (PostId postId) ctx.WebLog.id with
    | Some post ->
        let asOf = parseToUtc revDate
        return Some post, post.revisions |> List.tryFind (fun r -> r.asOf = asOf)
    | None -> return None, None
}

// GET /admin/post/{id}/revision/{revision-date}/preview
let previewRevision (postId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some _, Some rev ->
        return!
            Hash.FromAnonymousObject {|
                content = $"""<div class="mwl-revision-preview mb-3">{MarkupText.toHtml rev.text}</div>"""
            |}
            |> bareForTheme "admin" "" next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/revision/{revision-date}/restore
let restoreRevision (postId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some post, Some rev ->
        do! ctx.Data.Post.update
                { post with
                    revisions = { rev with asOf = DateTime.UtcNow }
                                  :: (post.revisions |> List.filter (fun r -> r.asOf <> rev.asOf))
                }
        do! addMessage ctx { UserMessage.success with message = "Revision restored successfully" }
        return! redirectToGet $"admin/post/{postId}/revisions" next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/revision/{revision-date}/delete
let deleteRevision (postId, revDate) : HttpHandler = fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some post, Some rev ->
        do! ctx.Data.Post.update { post with revisions = post.revisions |> List.filter (fun r -> r.asOf <> rev.asOf) }
        do! addMessage ctx { UserMessage.success with message = "Revision deleted successfully" }
        return! bareForTheme "admin" "" next ctx (Hash.FromAnonymousObject {| content = "" |})
    | None, _
    | _, None -> return! Error.notFound next ctx
}

#nowarn "3511"

// POST /admin/post/save
let save : HttpHandler = fun next ctx -> task {
    let! model  = ctx.BindFormAsync<EditPostModel> ()
    let  webLog = ctx.WebLog
    let  data   = ctx.Data
    let  now    = DateTime.UtcNow
    let! pst    = task {
        match model.postId with
        | "new" ->
            return Some
                { Post.empty with
                    id        = PostId.create ()
                    webLogId  = webLog.id
                    authorId  = ctx.UserId
                }
        | postId -> return! data.Post.findFullById (PostId postId) webLog.id
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
        let post = model.updatePost post revision now
        let post =
            match model.setPublished with
            | true ->
                let dt = parseToUtc (model.pubOverride.Value.ToString "o")
                match model.setUpdated with
                | true ->
                    { post with
                        publishedOn = Some dt
                        updatedOn   = dt
                        revisions   = [ { (List.head post.revisions) with asOf = dt } ]
                    }
                | false -> { post with publishedOn = Some dt }
            | false -> post
        do! (if model.postId = "new" then data.Post.add else data.Post.update) post
        // If the post was published or its categories changed, refresh the category cache
        if model.doPublish
           || not (pst.Value.categoryIds
                   |> List.append post.categoryIds
                   |> List.distinct
                   |> List.length = List.length pst.Value.categoryIds) then
            do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.success with message = "Post saved successfully" }
        return! redirectToGet $"admin/post/{PostId.toString post.id}/edit" next ctx
    | None -> return! Error.notFound next ctx
}
