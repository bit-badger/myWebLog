/// Handlers to manipulate posts
module MyWebLog.Handlers.Post

open System
open System.Collections.Generic
open MyWebLog

/// Parse a slug and page number from an "everything else" URL
let private parseSlugAndPage webLog (slugAndPage: string seq) =
    let fullPath = slugAndPage |> Seq.head
    let slugPath = slugAndPage |> Seq.skip 1 |> Seq.head
    let slugs, isFeed =
        let feedName = $"/{webLog.Rss.FeedName}"
        let notBlank = Array.filter (fun it -> it <> "")
        if (   (webLog.Rss.IsCategoryEnabled && fullPath.StartsWith "/category/")
            || (webLog.Rss.IsTagEnabled      && fullPath.StartsWith "/tag/"     ))
           && slugPath.EndsWith feedName then
            notBlank (slugPath.Replace(feedName, "").Split "/"), true
        else notBlank (slugPath.Split "/"), false
    let pageIdx = Array.IndexOf (slugs, "page")
    let pageNbr =
        match pageIdx with
        | -1 -> Some 1
        | idx when idx + 2 = slugs.Length -> Some (int slugs[pageIdx + 1])
        | _ -> None
    let slugParts = if pageIdx > 0 then Array.truncate pageIdx slugs else slugs
    pageNbr, String.Join("/", slugParts), isFeed

/// The type of post list being prepared
[<Struct>]
type ListType =
    | AdminList
    | CategoryList
    | PostList
    | SinglePost
    | TagList

open System.Threading.Tasks
open MyWebLog.Data
open MyWebLog.ViewModels

/// Convert a list of posts into items ready to be displayed
let preparePostList webLog posts listType (url: string) pageNbr perPage (data: IData) = task {
    let! authors     = getAuthors     webLog posts data
    let! tagMappings = getTagMappings webLog posts data
    let  relUrl it   = Some <| webLog.RelativeUrl(Permalink it)
    let  postItems   =
        posts
        |> Seq.ofList
        |> Seq.truncate perPage
        |> Seq.map (PostListItem.FromPost webLog)
        |> Array.ofSeq
    let! olderPost, newerPost =
        match listType with
        | SinglePost ->
            let post   = List.head posts
            let target = defaultArg post.PublishedOn post.UpdatedOn
            data.Post.FindSurroundingPosts webLog.Id target
        | _ -> Task.FromResult(None, None)
    let newerLink =
        match listType, pageNbr with
        | SinglePost,   _ -> newerPost |> Option.map (fun it -> string it.Permalink)
        | _,            1 -> None
        | PostList,     2    when webLog.DefaultPage = "posts" -> Some ""
        | PostList,     _ -> relUrl $"page/{pageNbr - 1}"
        | CategoryList, 2 -> relUrl $"category/{url}/"
        | CategoryList, _ -> relUrl $"category/{url}/page/{pageNbr - 1}"
        | TagList,      2 -> relUrl $"tag/{url}/"
        | TagList,      _ -> relUrl $"tag/{url}/page/{pageNbr - 1}"
        | AdminList,    2 -> relUrl  "admin/posts"
        | AdminList,    _ -> relUrl $"admin/posts/page/{pageNbr - 1}"
    let olderLink =
        match listType, List.length posts > perPage with
        | SinglePost,   _     -> olderPost |> Option.map (fun it -> string it.Permalink)
        | _,            false -> None
        | PostList,     true  -> relUrl $"page/{pageNbr + 1}"
        | CategoryList, true  -> relUrl $"category/{url}/page/{pageNbr + 1}"
        | TagList,      true  -> relUrl $"tag/{url}/page/{pageNbr + 1}"
        | AdminList,    true  -> relUrl $"admin/posts/page/{pageNbr + 1}"
    let model =
        { Posts      = postItems
          Authors    = authors
          Subtitle   = None
          NewerLink  = newerLink
          NewerName  = newerPost |> Option.map _.Title
          OlderLink  = olderLink
          OlderName  = olderPost |> Option.map _.Title
        }
    return
        makeHash {||}
        |> addToHash ViewContext.Model  model
        |> addToHash "tag_mappings"     tagMappings
        |> addToHash ViewContext.IsPost (match listType with SinglePost -> true | _ -> false)
}

open Giraffe

// GET /page/{pageNbr}
let pageOfPosts pageNbr : HttpHandler = fun next ctx -> task {
    let  count = ctx.WebLog.PostsPerPage
    let  data  = ctx.Data
    let! posts = data.Post.FindPageOfPublishedPosts ctx.WebLog.Id pageNbr count
    let! hash  = preparePostList ctx.WebLog posts PostList "" pageNbr count data
    let  title =
        match pageNbr, ctx.WebLog.DefaultPage with
        | 1, "posts" -> None
        | _, "posts" -> Some $"Page {pageNbr}"
        | _,  _      -> Some $"Page {pageNbr} &laquo; Posts"
    return!
        match title with Some ttl -> addToHash ViewContext.PageTitle ttl hash | None -> hash
        |> function
        | hash ->
            if pageNbr = 1 && ctx.WebLog.DefaultPage = "posts" then addToHash ViewContext.IsHome true hash else hash
        |> themedView "index" next ctx
}

// GET /page/{pageNbr}/
let redirectToPageOfPosts (pageNbr: int) : HttpHandler = fun next ctx ->
    redirectTo true (ctx.WebLog.RelativeUrl(Permalink $"page/{pageNbr}")) next ctx

// GET /category/{slug}/
// GET /category/{slug}/page/{pageNbr}
let pageOfCategorizedPosts slugAndPage : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    let data   = ctx.Data
    match parseSlugAndPage webLog slugAndPage with
    | Some pageNbr, slug, isFeed ->
        match CategoryCache.get ctx |> Array.tryFind (fun cat -> cat.Slug = slug) with
        | Some cat when isFeed ->
            return! Feed.generate (Feed.CategoryFeed ((CategoryId cat.Id), $"category/{slug}/{webLog.Rss.FeedName}"))
                        (defaultArg webLog.Rss.ItemsInFeed webLog.PostsPerPage) next ctx
        | Some cat ->
            // Category pages include posts in subcategories
            match! data.Post.FindPageOfCategorizedPosts webLog.Id (getCategoryIds slug ctx) pageNbr webLog.PostsPerPage
                with
            | posts when List.length posts > 0 ->
                let! hash = preparePostList webLog posts CategoryList cat.Slug pageNbr webLog.PostsPerPage data
                let pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
                return!
                       addToHash ViewContext.PageTitle      $"{cat.Name}: Category Archive{pgTitle}" hash
                    |> addToHash "subtitle"                 (defaultArg cat.Description "")
                    |> addToHash ViewContext.IsCategory     true
                    |> addToHash ViewContext.IsCategoryHome (pageNbr = 1)
                    |> addToHash ViewContext.Slug           slug
                    |> themedView "index" next ctx
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
            match! data.TagMap.FindByUrlValue urlTag webLog.Id with
            | Some m -> return m.Tag
            | None   -> return urlTag
        }
        if isFeed then
            return! Feed.generate (Feed.TagFeed(tag, $"tag/{rawTag}/{webLog.Rss.FeedName}"))
                        (defaultArg webLog.Rss.ItemsInFeed webLog.PostsPerPage) next ctx
        else
            match! data.Post.FindPageOfTaggedPosts webLog.Id tag pageNbr webLog.PostsPerPage with
            | posts when List.length posts > 0 ->
                let! hash    = preparePostList webLog posts TagList rawTag pageNbr webLog.PostsPerPage data
                let  pgTitle = if pageNbr = 1 then "" else $""" <small class="archive-pg-nbr">(Page {pageNbr})</small>"""
                return!
                       addToHash ViewContext.PageTitle $"Posts Tagged &ldquo;{tag}&rdquo;{pgTitle}" hash
                    |> addToHash ViewContext.IsTag     true
                    |> addToHash ViewContext.IsTagHome (pageNbr = 1)
                    |> addToHash ViewContext.Slug      rawTag
                    |> themedView "index" next ctx
            // Other systems use hyphens for spaces; redirect if this is an old tag link
            | _ ->
                let spacedTag = tag.Replace("-", " ")
                match! data.Post.FindPageOfTaggedPosts webLog.Id spacedTag pageNbr 1 with
                | posts when List.length posts > 0 ->
                    let endUrl = if pageNbr = 1 then "" else $"page/{pageNbr}"
                    return!
                        redirectTo true
                            (webLog.RelativeUrl(Permalink $"""tag/{spacedTag.Replace (" ", "+")}/{endUrl}"""))
                            next ctx
                | _ -> return! Error.notFound next ctx
    | None, _, _ -> return! Error.notFound next ctx
}

// GET /
let home : HttpHandler = fun next ctx -> task {
    let webLog = ctx.WebLog
    match webLog.DefaultPage with
    | "posts" -> return! pageOfPosts 1 next ctx
    | pageId ->
        match! ctx.Data.Page.FindById (PageId pageId) webLog.Id with
        | Some page ->
            return!
                hashForPage page.Title
                |> addToHash "page" (DisplayPage.FromPage webLog page)
                |> addToHash ViewContext.IsHome true
                |> themedView (defaultArg page.Template "single-page") next ctx
        | None -> return! Error.notFound next ctx
}

// GET /admin/posts
// GET /admin/posts/page/{pageNbr}
let all pageNbr : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let  data  = ctx.Data
    let! posts = data.Post.FindPageOfPosts ctx.WebLog.Id pageNbr 25
    let! hash  = preparePostList ctx.WebLog posts AdminList "" pageNbr 25 data
    return!
           addToHash ViewContext.PageTitle "Posts" hash
        |> withAntiCsrf ctx
        |> adminView "post-list" next ctx
}

// GET /admin/post/{id}/edit
let edit postId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let  data   = ctx.Data
    let! result = task {
        match postId with
        | "new" -> return Some ("Write a New Post", { Post.Empty with Id = PostId "new" })
        | _ ->
            match! data.Post.FindFullById (PostId postId) ctx.WebLog.Id with
            | Some post -> return Some ("Edit Post", post)
            | None -> return None
    }
    match result with
    | Some (title, post) when canEdit post.AuthorId ctx ->
        let! templates = templatesForTheme ctx "post"
        let  model     = EditPostModel.FromPost ctx.WebLog post
        return!
            hashForPage title
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model model
            |> addToHash "metadata" (
                Array.zip model.MetaNames model.MetaValues
                |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |]))
            |> addToHash "templates" templates
            |> addToHash "explicit_values" [|
                KeyValuePair.Create("", "&ndash; Default &ndash;")
                KeyValuePair.Create(string Yes,   "Yes")
                KeyValuePair.Create(string No,    "No")
                KeyValuePair.Create(string Clean, "Clean")
            |]
            |> adminView "post-edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/delete
let delete postId : HttpHandler = requireAccess WebLogAdmin >=> fun next ctx -> task {
    match! ctx.Data.Post.Delete (PostId postId) ctx.WebLog.Id with
    | true  -> do! addMessage ctx { UserMessage.Success with Message = "Post deleted successfully" }
    | false -> do! addMessage ctx { UserMessage.Error with Message = "Post not found; nothing deleted" }
    return! redirectToGet "admin/posts" next ctx
}

// GET /admin/post/{id}/permalinks
let editPermalinks postId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Post.FindFullById (PostId postId) ctx.WebLog.Id with
    | Some post when canEdit post.AuthorId ctx ->
        return!
            hashForPage "Manage Prior Permalinks"
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (ManagePermalinksModel.FromPost post)
            |> adminView "permalinks" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// POST /admin/post/permalinks
let savePermalinks : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model  = ctx.BindFormAsync<ManagePermalinksModel>()
    let  postId = PostId model.Id
    match! ctx.Data.Post.FindById postId ctx.WebLog.Id with
    | Some post when canEdit post.AuthorId ctx ->
        let links = model.Prior |> Array.map Permalink |> List.ofArray
        match! ctx.Data.Post.UpdatePriorPermalinks postId ctx.WebLog.Id links with
        | true ->
            do! addMessage ctx { UserMessage.Success with Message = "Post permalinks saved successfully" }
            return! redirectToGet $"admin/post/{model.Id}/permalinks" next ctx
        | false -> return! Error.notFound next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/revisions
let editRevisions postId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Post.FindFullById (PostId postId) ctx.WebLog.Id with
    | Some post when canEdit post.AuthorId ctx ->
        return!
            hashForPage "Manage Post Revisions"
            |> withAntiCsrf ctx
            |> addToHash ViewContext.Model (ManageRevisionsModel.FromPost ctx.WebLog post)
            |> adminView "revisions" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/revisions/purge
let purgeRevisions postId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let data = ctx.Data
    match! data.Post.FindFullById (PostId postId) ctx.WebLog.Id with
    | Some post when canEdit post.AuthorId ctx ->
        do! data.Post.Update { post with Revisions = [ List.head post.Revisions ] }
        do! addMessage ctx { UserMessage.Success with Message = "Prior revisions purged successfully" }
        return! redirectToGet $"admin/post/{postId}/revisions" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}

open Microsoft.AspNetCore.Http

/// Find the post and the requested revision
let private findPostRevision postId revDate (ctx: HttpContext) = task {
    match! ctx.Data.Post.FindFullById (PostId postId) ctx.WebLog.Id with
    | Some post ->
        let asOf = parseToUtc revDate
        return Some post, post.Revisions |> List.tryFind (fun r -> r.AsOf = asOf)
    | None -> return None, None
}

// GET /admin/post/{id}/revision/{revision-date}/preview
let previewRevision (postId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some post, Some rev when canEdit post.AuthorId ctx ->
        return! {|
            content =
                [   """<div class="mwl-revision-preview mb-3">"""
                    rev.Text.AsHtml() |> addBaseToRelativeUrls ctx.WebLog.ExtraPath
                    "</div>"
                ]
                |> String.concat ""
        |}
        |> makeHash |> adminBareView "" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/revision/{revision-date}/restore
let restoreRevision (postId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some post, Some rev when canEdit post.AuthorId ctx ->
        do! ctx.Data.Post.Update
                { post with
                    Revisions = { rev with AsOf = Noda.now () }
                                  :: (post.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf)) }
        do! addMessage ctx { UserMessage.Success with Message = "Revision restored successfully" }
        return! redirectToGet $"admin/post/{postId}/revisions" next ctx
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/revision/{revision-date}/delete
let deleteRevision (postId, revDate) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! findPostRevision postId revDate ctx with
    | Some post, Some rev when canEdit post.AuthorId ctx ->
        do! ctx.Data.Post.Update { post with Revisions = post.Revisions |> List.filter (fun r -> r.AsOf <> rev.AsOf) }
        do! addMessage ctx { UserMessage.Success with Message = "Revision deleted successfully" }
        return! adminBareView "" next ctx (makeHash {| content = "" |})
    | Some _, Some _ -> return! Error.notAuthorized next ctx
    | None, _
    | _, None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/chapters
let chapters postId : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Post.FindById (PostId postId) ctx.WebLog.Id with
    | Some post
        when    Option.isSome post.Episode
             && Option.isSome post.Episode.Value.Chapters
             && canEdit post.AuthorId ctx ->
        return!
            adminPage "Manage Chapters" true (AdminViews.Post.chapters false (ManageChaptersModel.Create post)) next ctx
    | Some _ | None -> return! Error.notFound next ctx
}

// GET /admin/post/{id}/chapter/{idx}
let editChapter (postId, index) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    match! ctx.Data.Post.FindById (PostId postId) ctx.WebLog.Id with
    | Some post
        when    Option.isSome post.Episode
             && Option.isSome post.Episode.Value.Chapters
             && canEdit post.AuthorId ctx ->
        let chapter =
            if index = -1 then Some Chapter.Empty
            else
                let chapters = post.Episode.Value.Chapters.Value
                if index < List.length chapters then Some chapters[index] else None
        match chapter with
        | Some chap ->
            return!
                adminPage
                    (if index = -1 then "Add a Chapter" else "Edit Chapter") true
                    (AdminViews.Post.chapterEdit (EditChapterModel.FromChapter post.Id index chap)) next ctx
        | None -> return! Error.notFound next ctx
    | Some _ | None -> return! Error.notFound next ctx
}

// POST /admin/post/{id}/chapter/{idx}
let saveChapter (postId, index) : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let data = ctx.Data
    match! data.Post.FindById (PostId postId) ctx.WebLog.Id with
    | Some post
        when    Option.isSome post.Episode
             && Option.isSome post.Episode.Value.Chapters
             && canEdit post.AuthorId ctx ->
        let! form     = ctx.BindFormAsync<EditChapterModel>()
        let  chapters = post.Episode.Value.Chapters.Value
        if index >= -1 && index < List.length chapters then
            try
                let chapter     = form.ToChapter()
                let existing    = if index = -1 then chapters else chapters |> List.removeAt index
                let updatedPost =
                    { post with
                        Episode = Some
                          { post.Episode.Value with
                              Chapters = Some (chapter :: existing |> List.sortBy _.StartTime) } }
                do! data.Post.Update updatedPost
                do! addMessage ctx { UserMessage.Success with Message = "Chapter saved successfully" }
                return!
                    adminPage
                        "Manage Chapters" true
                        (AdminViews.Post.chapterList form.AddAnother (ManageChaptersModel.Create updatedPost)) next ctx
            with
            | ex -> return! Error.notFound next ctx // TODO: return error
        else return! Error.notFound next ctx
    | Some _ | None -> return! Error.notFound next ctx
}

// POST /admin/post/save
let save : HttpHandler = requireAccess Author >=> fun next ctx -> task {
    let! model   = ctx.BindFormAsync<EditPostModel>()
    let  data    = ctx.Data
    let  tryPost =
        if model.IsNew then
            { Post.Empty with
                Id        = PostId.Create()
                WebLogId  = ctx.WebLog.Id
                AuthorId  = ctx.UserId }
            |> someTask
        else data.Post.FindFullById (PostId model.PostId) ctx.WebLog.Id
    match! tryPost with
    | Some post when canEdit post.AuthorId ctx ->
        let priorCats   = post.CategoryIds
        let updatedPost =
            model.UpdatePost post (Noda.now ())
            |> function
            | post ->
                if model.SetPublished then
                    let dt = parseToUtc (model.PubOverride.Value.ToString "o")
                    if model.SetUpdated then
                        { post with
                            PublishedOn = Some dt
                            UpdatedOn   = dt
                            Revisions   = [ { (List.head post.Revisions) with AsOf = dt } ] }
                    else { post with PublishedOn = Some dt }
                else post
        do! (if model.PostId = "new" then data.Post.Add else data.Post.Update) updatedPost
        // If the post was published or its categories changed, refresh the category cache
        if model.DoPublish
           || not (priorCats
                   |> List.append updatedPost.CategoryIds
                   |> List.distinct
                   |> List.length = List.length priorCats) then
            do! CategoryCache.update ctx
        do! addMessage ctx { UserMessage.Success with Message = "Post saved successfully" }
        return! redirectToGet $"admin/post/{post.Id}/edit" next ctx
    | Some _ -> return! Error.notAuthorized next ctx
    | None -> return! Error.notFound next ctx
}
