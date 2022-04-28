﻿[<RequireQualifiedAccess>]
module MyWebLog.Handlers

open DotLiquid
open Giraffe
open Microsoft.AspNetCore.Http
open MyWebLog
open MyWebLog.ViewModels
open RethinkDb.Driver.Net
open System
open System.Net
open System.Threading.Tasks

/// Handlers for error conditions
module Error =

    (* open Microsoft.Extensions.Logging *)

    (*/// Handle errors
    let error (ex : Exception) (log : ILogger) =
        log.LogError (EventId(), ex, "An unhandled exception has occurred while executing the request.")
        clearResponse
        >=> setStatusCode 500
        >=> setHttpHeader "X-Toast" (sprintf "error|||%s: %s" (ex.GetType().Name) ex.Message)
        >=> text ex.Message *)

    /// Handle unauthorized actions, redirecting to log on for GETs, otherwise returning a 401 Not Authorized response
    let notAuthorized : HttpHandler = fun next ctx ->
        (next, ctx)
        ||> match ctx.Request.Method with
            | "GET" -> redirectTo false $"/user/log-on?returnUrl={WebUtility.UrlEncode ctx.Request.Path}"
            | _ -> setStatusCode 401 >=> fun _ _ -> Task.FromResult<HttpContext option> None

    /// Handle 404s from the API, sending known URL paths to the Vue app so that they can be handled there
    let notFound : HttpHandler =
        setStatusCode 404 >=> text "Not found"


open System.Text.Json

/// Session extensions to get and set objects
type ISession with
    
    /// Set an item in the session
    member this.Set<'T> (key, item : 'T) =
        this.SetString (key, JsonSerializer.Serialize item)
    
    /// Get an item from the session
    member this.Get<'T> key =
        match this.GetString key with
        | null -> None
        | item -> Some (JsonSerializer.Deserialize<'T> item)

    
open System.Collections.Generic

[<AutoOpen>]
module private Helpers =
    
    open Microsoft.AspNetCore.Antiforgery
    open Microsoft.Extensions.Configuration
    open Microsoft.Extensions.DependencyInjection
    open System.Security.Claims
    open System.IO

    /// The HTTP item key for loading the session
    let private sessionLoadedKey = "session-loaded"
    
    /// Load the session if it has not been loaded already; ensures async access but not excessive loading
    let private loadSession (ctx : HttpContext) = task {
        if not (ctx.Items.ContainsKey sessionLoadedKey) then
            do! ctx.Session.LoadAsync ()
            ctx.Items.Add (sessionLoadedKey, "yes")
    }
    
    /// Ensure that the session is committed
    let private commitSession (ctx : HttpContext) = task {
        if ctx.Items.ContainsKey sessionLoadedKey then do! ctx.Session.CommitAsync ()
    }
    
    /// Add a message to the user's session
    let addMessage (ctx : HttpContext) message = task {
        do! loadSession ctx
        let msg = match ctx.Session.Get<UserMessage list> "messages" with Some it -> it | None -> []
        ctx.Session.Set ("messages", message :: msg)
    }
    
    /// Get any messages from the user's session, removing them in the process
    let messages (ctx : HttpContext) = task {
        do! loadSession ctx
        match ctx.Session.Get<UserMessage list> "messages" with
        | Some msg ->
            ctx.Session.Remove "messages"
            return msg |> (List.rev >> Array.ofList)
        | None -> return [||]
    }
    
    /// Hold variable for the configured generator string
    let mutable private generatorString : string option = None
    
    /// Get the generator string
    let private generator (ctx : HttpContext) =
        if Option.isNone generatorString then
            let cfg = ctx.RequestServices.GetRequiredService<IConfiguration> ()
            generatorString <- Option.ofObj cfg["Generator"]
        match generatorString with Some gen -> gen | None -> "generator not configured"
    
    /// Either get the web log from the hash, or get it from the cache and add it to the hash
    let private deriveWebLogFromHash (hash : Hash) ctx =
        match hash.ContainsKey "web_log" with
        | true -> hash["web_log"] :?> WebLog
        | false ->
            let wl = WebLogCache.get ctx
            hash.Add ("web_log", wl)
            wl
    
    /// Render a view for the specified theme, using the specified template, layout, and hash
    let viewForTheme theme template next ctx = fun (hash : Hash) -> task {
        // Don't need the web log, but this adds it to the hash if the function is called directly
        let _ = deriveWebLogFromHash hash ctx
        let! messages = messages ctx
        hash.Add ("logged_on",    ctx.User.Identity.IsAuthenticated)
        hash.Add ("page_list",    PageListCache.get ctx)
        hash.Add ("current_page", ctx.Request.Path.Value.Substring 1)
        hash.Add ("messages",     messages)
        hash.Add ("generator",    generator ctx)
        
        do! commitSession ctx
        
        // NOTE: DotLiquid does not support {% render %} or {% include %} in its templates, so we will do a two-pass
        //       render; the net effect is a "layout" capability similar to Razor or Pug
        
        // Render view content...
        let! contentTemplate = TemplateCache.get theme template
        hash.Add ("content", contentTemplate.Render hash)
        
        // ...then render that content with its layout
        let! layoutTemplate = TemplateCache.get theme "layout"
        
        return! htmlString (layoutTemplate.Render hash) next ctx
    }
    
    /// Return a view for the web log's default theme
    let themedView template next ctx = fun (hash : Hash) -> task {
        return! viewForTheme (deriveWebLogFromHash hash ctx).themePath template next ctx hash
    }
    
    /// Redirect after doing some action; commits session and issues a temporary redirect
    let redirectToGet url : HttpHandler = fun next ctx -> task {
        do! commitSession ctx
        return! redirectTo false url next ctx
    }
    
    /// Get the web log ID for the current request
    let webLogId ctx = (WebLogCache.get ctx).id
    
    /// Get the user ID for the current request
    let userId (ctx : HttpContext) =
        WebLogUserId (ctx.User.Claims |> Seq.find (fun c -> c.Type = ClaimTypes.NameIdentifier)).Value
        
    /// Get the RethinkDB connection
    let conn (ctx : HttpContext) = ctx.RequestServices.GetRequiredService<IConnection> ()
    
    /// Get the Anti-CSRF service
    let private antiForgery (ctx : HttpContext) = ctx.RequestServices.GetRequiredService<IAntiforgery> ()
    
    /// Get the cross-site request forgery token set
    let csrfToken (ctx : HttpContext) =
        (antiForgery ctx).GetAndStoreTokens ctx
    
    /// Validate the cross-site request forgery token in the current request
    let validateCsrf : HttpHandler = fun next ctx -> task {
        match! (antiForgery ctx).IsRequestValidAsync ctx with
        | true -> return! next ctx
        | false -> return! RequestErrors.BAD_REQUEST "CSRF token invalid" next ctx
    }
    
    /// Require a user to be logged on
    let requireUser = requiresAuthentication Error.notAuthorized
    
    /// Get the templates available for the current web log's theme (in a key/value pair list)
    let templatesForTheme ctx (typ : string) =
        seq {
            KeyValuePair.Create ("", $"- Default (single-{typ}) -")
            yield!
                Path.Combine ("themes", (WebLogCache.get ctx).themePath)
                |> Directory.EnumerateFiles
                |> Seq.filter (fun it -> it.EndsWith $"{typ}.liquid")
                |> Seq.map (fun it ->
                    let parts    = it.Split Path.DirectorySeparatorChar
                    let template = parts[parts.Length - 1].Replace (".liquid", "")
                    KeyValuePair.Create (template, template))
        }
        |> Array.ofSeq
    

/// Handlers to manipulate admin functions
module Admin =
    
    open System.IO
    
    /// The currently available themes
    let private themes () =
        Directory.EnumerateDirectories "themes"
        |> Seq.map (fun it -> it.Split Path.DirectorySeparatorChar |> Array.last)
        |> Seq.filter (fun it -> it <> "admin")
        |> Seq.map (fun it -> KeyValuePair.Create (it, it))
        |> Array.ofSeq
        
    // GET /admin
    let dashboard : HttpHandler = requireUser >=> fun next ctx -> task {
        let webLogId = webLogId ctx
        let conn     = conn ctx
        let getCount (f : WebLogId -> IConnection -> Task<int>) = f webLogId conn
        let! posts   = Data.Post.countByStatus Published |> getCount
        let! drafts  = Data.Post.countByStatus Draft     |> getCount
        let! pages   = Data.Page.countAll                |> getCount
        let! listed  = Data.Page.countListed             |> getCount
        let! cats    = Data.Category.countAll            |> getCount
        let! topCats = Data.Category.countTopLevel       |> getCount
        return!
            Hash.FromAnonymousObject
                {| page_title = "Dashboard"
                   model =
                       { posts              = posts
                         drafts             = drafts
                         pages              = pages
                         listedPages        = listed
                         categories         = cats
                         topLevelCategories = topCats
                       }
                |}
            |> viewForTheme "admin" "dashboard" next ctx
    }
    
    // GET /admin/settings
    let settings : HttpHandler = requireUser >=> fun next ctx -> task {
        let  webLog   = WebLogCache.get ctx
        let! allPages = Data.Page.findAll webLog.id (conn ctx)
        return!
            Hash.FromAnonymousObject
                {|  csrf  = csrfToken ctx
                    model = SettingsModel.fromWebLog webLog
                    pages =
                        seq {
                            KeyValuePair.Create ("posts", "- First Page of Posts -")
                            yield! allPages
                                   |> List.sortBy (fun p -> p.title.ToLower ())
                                   |> List.map (fun p -> KeyValuePair.Create (PageId.toString p.id, p.title))
                        }
                        |> Array.ofSeq
                    themes     = themes ()
                    web_log    = webLog
                    page_title = "Web Log Settings"
                |}
            |> viewForTheme "admin" "settings" next ctx
    }
    
    // POST /admin/settings
    let saveSettings : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
        let  conn  = conn ctx
        let! model = ctx.BindFormAsync<SettingsModel> ()
        match! Data.WebLog.findById (WebLogCache.get ctx).id conn with
        | Some webLog ->
            let updated =
                { webLog with
                    name         = model.name
                    subtitle     = if model.subtitle = "" then None else Some model.subtitle
                    defaultPage  = model.defaultPage
                    postsPerPage = model.postsPerPage
                    timeZone     = model.timeZone
                    themePath    = model.themePath
                }
            do! Data.WebLog.updateSettings updated conn

            // Update cache
            WebLogCache.set ctx updated
        
            do! addMessage ctx { UserMessage.success with message = "Web log settings saved successfully" }
            return! redirectToGet "/admin" next ctx
        | None -> return! Error.notFound next ctx
    }


/// Handlers to manipulate categories
module Category =
    
    // GET /categories
    let all : HttpHandler = requireUser >=> fun next ctx -> task {
        return!
            Hash.FromAnonymousObject {|
                categories = CategoryCache.get ctx
                page_title = "Categories"
                csrf       = csrfToken ctx
            |}
            |> viewForTheme "admin" "category-list" next ctx
    }
    
    // GET /category/{id}/edit
    let edit catId : HttpHandler = requireUser >=> fun next ctx -> task {
        let  webLogId = webLogId ctx
        let  conn     = conn     ctx
        let! result   = task {
            match catId with
            | "new" -> return Some ("Add a New Category", { Category.empty with id = CategoryId "new" })
            | _ ->
                match! Data.Category.findById (CategoryId catId) webLogId conn with
                | Some cat -> return Some ("Edit Category", cat)
                | None -> return None
        }
        match result with
        | Some (title, cat) ->
            return!
                Hash.FromAnonymousObject {|
                    csrf       = csrfToken ctx
                    model      = EditCategoryModel.fromCategory cat
                    page_title = title
                    categories = CategoryCache.get ctx
                |}
                |> viewForTheme "admin" "category-edit" next ctx
        | None -> return! Error.notFound next ctx
    }
    
    // POST /category/save
    let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
        let! model    = ctx.BindFormAsync<EditCategoryModel> ()
        let  webLogId = webLogId ctx
        let  conn     = conn     ctx
        let! category = task {
            match model.categoryId with
            | "new" -> return Some { Category.empty with id = CategoryId.create (); webLogId = webLogId }
            | catId -> return! Data.Category.findById (CategoryId catId) webLogId conn
        }
        match category with
        | Some cat ->
            let cat =
                { cat with
                    name        = model.name
                    slug        = model.slug
                    description = if model.description = "" then None else Some model.description
                    parentId    = if model.parentId    = "" then None else Some (CategoryId model.parentId)
                }
            do! (match model.categoryId with "new" -> Data.Category.add | _ -> Data.Category.update) cat conn
            do! CategoryCache.update ctx
            do! addMessage ctx { UserMessage.success with message = "Category saved successfully" }
            return! redirectToGet $"/category/{CategoryId.toString cat.id}/edit" next ctx
        | None -> return! Error.notFound next ctx
    }
    
    // POST /category/{id}/delete
    let delete catId : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
        let webLogId = webLogId ctx
        let conn     = conn     ctx
        match! Data.Category.delete (CategoryId catId) webLogId conn with
        | true ->
            do! CategoryCache.update ctx
            do! addMessage ctx { UserMessage.success with message = "Category deleted successfully" }
        | false -> do! addMessage ctx { UserMessage.error with message = "Category not found; cannot delete" }
        return! redirectToGet "/categories" next ctx
    }

    
/// Handlers to manipulate pages
module Page =
    
    // GET /pages
    // GET /pages/page/{pageNbr}
    let all pageNbr : HttpHandler = requireUser >=> fun next ctx -> task {
        let  webLog = WebLogCache.get ctx
        let! pages  = Data.Page.findPageOfPages webLog.id pageNbr (conn ctx)
        return!
            Hash.FromAnonymousObject
                {| pages      = pages |> List.map (DisplayPage.fromPageMinimal webLog)
                   page_title = "Pages"
                |}
            |> viewForTheme "admin" "page-list" next ctx
    }

    // GET /page/{id}/edit
    let edit pgId : HttpHandler = requireUser >=> fun next ctx -> task {
        let! result = task {
            match pgId with
            | "new" -> return Some ("Add a New Page", { Page.empty with id = PageId "new" })
            | _ ->
                match! Data.Page.findByFullId (PageId pgId) (webLogId ctx) (conn ctx) with
                | Some page -> return Some ("Edit Page", page)
                | None -> return None
        }
        match result with
        | Some (title, page) ->
            let model = EditPageModel.fromPage page
            return!
                Hash.FromAnonymousObject {|
                    csrf       = csrfToken ctx
                    model      = model
                    metadata   = Array.zip model.metaNames model.metaValues
                                 |> Array.mapi (fun idx (name, value) -> [| string idx; name; value |])
                    page_title = title
                    templates  = templatesForTheme ctx "page"
                |}
                |> viewForTheme "admin" "page-edit" next ctx
        | None -> return! Error.notFound next ctx
    }

    // POST /page/save
    let save : HttpHandler = requireUser >=> validateCsrf >=> fun next ctx -> task {
        let! model    = ctx.BindFormAsync<EditPageModel> ()
        let  webLogId = webLogId ctx
        let  conn     = conn ctx
        let  now      = DateTime.UtcNow
        let! pg       = task {
            match model.pageId with
            | "new" ->
                return Some
                    { Page.empty with
                        id          = PageId.create ()
                        webLogId    = webLogId
                        authorId    = userId ctx
                        publishedOn = now
                    }
            | pgId -> return! Data.Page.findByFullId (PageId pgId) webLogId conn
        }
        match pg with
        | Some page ->
            let updateList = page.showInPageList <> model.isShownInPageList
            let revision   = { asOf = now; text = MarkupText.parse $"{model.source}: {model.text}" }
            // Detect a permalink change, and add the prior one to the prior list
            let page =
                match Permalink.toString page.permalink with
                | "" -> page
                | link when link = model.permalink -> page
                | _ -> { page with priorPermalinks = page.permalink :: page.priorPermalinks }
            let page =
                { page with
                    title          = model.title
                    permalink      = Permalink model.permalink
                    updatedOn      = now
                    showInPageList = model.isShownInPageList
                    template       = match model.template with "" -> None | tmpl -> Some tmpl
                    text           = MarkupText.toHtml revision.text
                    metadata       = Seq.zip model.metaNames model.metaValues
                                     |> Seq.filter (fun it -> fst it > "")
                                     |> Seq.map (fun it -> { name = fst it; value = snd it })
                                     |> Seq.sortBy (fun it -> $"{it.name.ToLower ()} {it.value.ToLower ()}")
                                     |> List.ofSeq
                    revisions      = match page.revisions |> List.tryHead with
                                     | Some r when r.text = revision.text -> page.revisions
                                     | _ -> revision :: page.revisions
                }
            do! (match model.pageId with "new" -> Data.Page.add | _ -> Data.Page.update) page conn
            if updateList then do! PageListCache.update ctx
            do! addMessage ctx { UserMessage.success with message = "Page saved successfully" }
            return! redirectToGet $"/page/{PageId.toString page.id}/edit" next ctx
        | None -> return! Error.notFound next ctx
    }


/// Handlers to manipulate posts
module Post =
    
    /// The type of post list being prepared
    type ListType =
        | CategoryList
        | TagList
        | PostList
        | SinglePost
        | AdminList
        
    /// Convert a list of posts into items ready to be displayed
    let private preparePostList (webLog : WebLog) (posts : Post list) listType pageNbr perPage ctx conn = task {
        let! authors =
            posts
            |> List.map (fun p -> p.authorId)
            |> List.distinct
            |> Data.WebLogUser.findNames webLog.id conn
        let postItems =
            posts
            |> Seq.ofList
            |> Seq.truncate perPage
            |> Seq.map (PostListItem.fromPost webLog)
            |> Array.ofSeq
        let newerLink =
            match listType, pageNbr with
            | SinglePost,   _  -> Some "TODO: retrieve prior post"
            | _,            1L -> None
            | PostList,     2L    when webLog.defaultPage = "posts" -> Some ""
            | PostList,     _  -> Some $"page/{pageNbr - 1L}"
            | CategoryList, _  -> Some "TODO"
            | TagList,      _  -> Some "TODO"
            | AdminList,    2L -> Some "posts"
            | AdminList,    _  -> Some $"posts/page/{pageNbr - 1L}"
        let olderLink =
            match listType, List.length posts > perPage with
            | SinglePost,   _     -> Some "TODO: retrieve next post"
            | _,            false -> None
            | PostList,     true  -> Some $"page/{pageNbr + 1L}"
            | CategoryList, true  -> Some $"category/TODO-slug-goes-here/page/{pageNbr + 1L}"
            | TagList,      true  -> Some $"tag/TODO-slug-goes-here/page/{pageNbr + 1L}"
            | AdminList,    true  -> Some $"posts/page/{pageNbr + 1L}"
        let model =
            { posts      = postItems
              authors    = authors
              subtitle   = None
              newerLink  = newerLink
              olderLink  = olderLink
            }
        return Hash.FromAnonymousObject {| model = model; categories = CategoryCache.get ctx |}
    }
    
    // GET /page/{pageNbr}
    let pageOfPosts pageNbr : HttpHandler = fun next ctx -> task {
        let  webLog = WebLogCache.get ctx
        let  conn   = conn ctx
        let! posts  = Data.Post.findPageOfPublishedPosts webLog.id pageNbr webLog.postsPerPage conn
        let! hash   = preparePostList webLog posts PostList pageNbr webLog.postsPerPage ctx conn
        let  title  =
            match pageNbr, webLog.defaultPage with
            | 1L, "posts" -> None
            | _,  "posts" -> Some $"Page {pageNbr}"
            | _,  _       -> Some $"Page {pageNbr} &laquo; Posts"
        match title with Some ttl -> hash.Add ("page_title", ttl) | None -> ()
        return! themedView "index" next ctx hash
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
                    Hash.FromAnonymousObject {| page = DisplayPage.fromPage webLog page; page_title = page.title |}
                    |> themedView (defaultArg page.template "single-page") next ctx
            | None -> return! Error.notFound next ctx
    }
    
    // GET {**link}
    let catchAll : HttpHandler = fun next ctx -> task {
        let webLog    = WebLogCache.get ctx
        let conn      = conn ctx
        let permalink = (string >> Permalink) ctx.Request.RouteValues["link"]
        // Current post
        match! Data.Post.findByPermalink permalink webLog.id conn with
        | Some post ->
            let! model = preparePostList webLog [ post ] SinglePost 1 1 ctx conn
            model.Add ("page_title", post.title)
            return! themedView "single-post" next ctx model
        | None ->
            // Current page
            match! Data.Page.findByPermalink permalink webLog.id conn with
            | Some page ->
                return!
                    Hash.FromAnonymousObject {| page = DisplayPage.fromPage webLog page; page_title = page.title |}
                    |> themedView (defaultArg page.template "single-page") next ctx
            | None ->
                // Prior post
                match! Data.Post.findCurrentPermalink permalink webLog.id conn with
                | Some link -> return! redirectTo true $"/{Permalink.toString link}" next ctx
                | None ->
                    // Prior page
                    match! Data.Page.findCurrentPermalink permalink webLog.id conn with
                    | Some link -> return! redirectTo true $"/{Permalink.toString link}" next ctx
                    | None ->
                        // We tried, we really did...
                        Console.Write($"Returning 404 for permalink |{permalink}|");
                        return! Error.notFound next ctx
    }

    // GET /posts
    // GET /posts/page/{pageNbr}
    let all pageNbr : HttpHandler = requireUser >=> fun next ctx -> task {
        let  webLog = WebLogCache.get ctx
        let  conn   = conn ctx
        let! posts  = Data.Post.findPageOfPosts webLog.id pageNbr 25 conn
        let! hash   = preparePostList webLog posts AdminList pageNbr 25 ctx conn
        hash.Add ("page_title", "Posts")
        return! viewForTheme "admin" "post-list" next ctx hash
    }
    
    // GET /post/{id}/edit
    let edit postId : HttpHandler = requireUser >=> fun next ctx -> task {
        let  webLogId = webLogId ctx
        let  conn     = conn     ctx
        let! result   = task {
            match postId with
            | "new" -> return Some ("Write a New Post", { Post.empty with id = PostId "new" })
            | _ ->
                match! Data.Post.findByFullId (PostId postId) webLogId conn with
                | Some post -> return Some ("Edit Post", post)
                | None -> return None
        }
        match result with
        | Some (title, post) ->
            let! cats = Data.Category.findAllForView webLogId conn
            return!
                Hash.FromAnonymousObject {|
                    csrf       = csrfToken ctx
                    model      = EditPostModel.fromPost post
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


/// Handlers to manipulate users
module User =
    
    open Microsoft.AspNetCore.Authentication;
    open Microsoft.AspNetCore.Authentication.Cookies
    open System.Security.Claims
    open System.Security.Cryptography
    open System.Text
    
    /// Hash a password for a given user
    let hashedPassword (plainText : string) (email : string) (salt : Guid) =
        let allSalt = Array.concat [ salt.ToByteArray (); Encoding.UTF8.GetBytes email ] 
        use alg     = new Rfc2898DeriveBytes (plainText, allSalt, 2_048)
        Convert.ToBase64String (alg.GetBytes 64)
    
    // GET /user/log-on
    let logOn returnUrl : HttpHandler = fun next ctx -> task {
        let returnTo =
            match returnUrl with
            | Some _ -> returnUrl
            | None ->
                match ctx.Request.Query.ContainsKey "returnUrl" with
                | true -> Some ctx.Request.Query["returnUrl"].[0]
                | false -> None
        return!
            Hash.FromAnonymousObject {|
                model      = { LogOnModel.empty with returnTo = returnTo }
                page_title = "Log On"
                csrf       = csrfToken ctx
            |}
            |> viewForTheme "admin" "log-on" next ctx
    }
    
    // POST /user/log-on
    let doLogOn : HttpHandler = validateCsrf >=> fun next ctx -> task {
        let! model  = ctx.BindFormAsync<LogOnModel> ()
        let  webLog = WebLogCache.get ctx
        match! Data.WebLogUser.findByEmail model.emailAddress webLog.id (conn ctx) with 
        | Some user when user.passwordHash = hashedPassword model.password user.userName user.salt ->
            let claims = seq {
                Claim (ClaimTypes.NameIdentifier, WebLogUserId.toString user.id)
                Claim (ClaimTypes.Name,           $"{user.firstName} {user.lastName}")
                Claim (ClaimTypes.GivenName,      user.preferredName)
                Claim (ClaimTypes.Role,           user.authorizationLevel.ToString ())
            }
            let identity = ClaimsIdentity (claims, CookieAuthenticationDefaults.AuthenticationScheme)

            do! ctx.SignInAsync (identity.AuthenticationType, ClaimsPrincipal identity,
                AuthenticationProperties (IssuedUtc = DateTimeOffset.UtcNow))
            do! addMessage ctx
                    { UserMessage.success with message = $"Logged on successfully | Welcome to {webLog.name}!" }
            return! redirectToGet (match model.returnTo with Some url -> url | None -> "/admin") next ctx
        | _ ->
            do! addMessage ctx { UserMessage.error with message = "Log on attempt unsuccessful" }
            return! logOn model.returnTo next ctx
    }

    // GET /user/log-off
    let logOff : HttpHandler = fun next ctx -> task {
        do! ctx.SignOutAsync CookieAuthenticationDefaults.AuthenticationScheme
        do! addMessage ctx { UserMessage.info with message = "Log off successful" }
        return! redirectToGet "/" next ctx
    }
    

open Giraffe.EndpointRouting

/// The endpoints defined in the above handlers
let endpoints = [
    GET [
        route "/" Post.home
    ]
    subRoute "/admin" [
        GET [
            route ""          Admin.dashboard
            route "/settings" Admin.settings
        ]
        POST [
            route "/settings" Admin.saveSettings
        ]
    ]
    subRoute "/categor" [
        GET [
            route  "ies"       Category.all
            routef "y/%s/edit" Category.edit
        ]
        POST [
            route  "y/save"      Category.save
            routef "y/%s/delete" Category.delete
        ]
    ]
    subRoute "/page" [
        GET [
            routef "/%d"       Post.pageOfPosts
            routef "/%s/edit"  Page.edit
            route  "s"         (Page.all 1)
            routef "s/page/%d" Page.all
        ]
        POST [
            route "/save" Page.save
        ]
    ]
    subRoute "/post" [
        GET [
            routef "/%s/edit"  Post.edit
            route  "s"         (Post.all 1)
            routef "s/page/%d" Post.all
        ]
        POST [
            route "/save" Post.save
        ]
    ]
    subRoute "/user" [
        GET [
            route "/log-on"  (User.logOn None)
            route "/log-off" User.logOff
        ]
        POST [
            route "/log-on" User.doLogOn
        ]
    ]
    route "{**link}" Post.catchAll
]
