namespace MyWebLog.Features.Posts

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Mvc
open MyWebLog
open MyWebLog.Features.Pages
open MyWebLog.Features.Shared
open System
open System.Threading.Tasks

/// Handle post-related requests
[<Route "/post">]
[<Authorize>]
type PostController () =
    inherit MyWebLogController ()

    [<HttpGet "~/">]
    [<AllowAnonymous>]
    member this.Index () = task {
        match this.WebLog.defaultPage with
        | "posts" -> return! this.PageOfPosts 1
        | pageId -> 
            match! Data.Page.findById (PageId pageId) this.WebLog.id this.Db with
            | Some page ->
                return this.ThemedView (defaultArg page.template "SinglePage", SinglePageModel (page, this.WebLog))
            | None -> return this.NotFound ()
    }

    [<HttpGet "~/page/{pageNbr:int}">]
    [<AllowAnonymous>]
    member this.PageOfPosts (pageNbr : int) = task {
        let! posts = Data.Post.findPageOfPublishedPosts this.WebLog.id pageNbr this.WebLog.postsPerPage this.Db
        return this.ThemedView ("Index", MultiplePostModel (posts, this.WebLog))
    }

    [<HttpGet "~/{*link}">]
    member this.CatchAll (link : string) = task {
        let permalink = Permalink link
        match! Data.Post.findByPermalink permalink this.WebLog.id this.Db with
        | Some post -> return this.NotFound ()
            // TODO: return via single-post action
        | None ->
            match! Data.Page.findByPermalink permalink this.WebLog.id this.Db with
            | Some page ->
                return this.ThemedView (defaultArg page.template "SinglePage", SinglePageModel (page, this.WebLog))
            | None ->

                // TOOD: search prior permalinks for posts and pages

                // We tried, we really tried...
                Console.Write($"Returning 404 for permalink |{permalink}|");
                return this.NotFound ()
    }

    [<HttpGet "all">]
    member this.All () = task {
        do! Task.CompletedTask;
        NotImplementedException () |> raise
    }

    [<HttpGet "{id}/edit">]
    member this.Edit(postId : string) = task {
        do! Task.CompletedTask;
        NotImplementedException () |> raise
    }
