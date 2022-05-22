/// Custom DotLiquid filters and tags
module MyWebLog.DotLiquidBespoke

open System
open System.IO
open DotLiquid
open MyWebLog.ViewModels

/// Get the current web log from the DotLiquid context
let webLog (ctx : Context) =
    ctx.Environments[0].["web_log"] :?> WebLog

/// Obtain the link from known types
let permalink (ctx : Context) (item : obj) (linkFunc : WebLog -> Permalink -> string) =
    match item with
    | :? String       as link  -> Some link
    | :? DisplayPage  as page  -> Some page.permalink
    | :? PostListItem as post  -> Some post.permalink
    | :? DropProxy    as proxy -> Option.ofObj proxy["permalink"] |> Option.map string
    | _ -> None
    |> function
    | Some link -> linkFunc (webLog ctx) (Permalink link)
    | None      -> $"alert('unknown item type {item.GetType().Name}')"

/// A filter to generate an absolute link
type AbsoluteLinkFilter () =
    static member AbsoluteLink (ctx : Context, item : obj) =
        permalink ctx item WebLog.absoluteUrl

/// A filter to generate a link with posts categorized under the given category
type CategoryLinkFilter () =
    static member CategoryLink (ctx : Context, catObj : obj) =
        match catObj with
        | :? DisplayCategory as cat   -> Some cat.slug
        | :? DropProxy       as proxy -> Option.ofObj proxy["slug"] |> Option.map string
        | _ -> None
        |> function
        | Some slug -> WebLog.relativeUrl (webLog ctx) (Permalink $"category/{slug}/")
        | None      -> $"alert('unknown category object type {catObj.GetType().Name}')"
        

/// A filter to generate a link that will edit a page
type EditPageLinkFilter () =
    static member EditPageLink (ctx : Context, pageObj : obj) =
        match pageObj with
        | :? DisplayPage as page  -> Some page.id
        | :? DropProxy   as proxy -> Option.ofObj proxy["id"] |> Option.map string
        | :? String      as theId -> Some theId
        | _ -> None
        |> function
        | Some pageId -> WebLog.relativeUrl (webLog ctx) (Permalink $"admin/page/{pageId}/edit")
        | None        -> $"alert('unknown page object type {pageObj.GetType().Name}')"
    
/// A filter to generate a link that will edit a post
type EditPostLinkFilter () =
    static member EditPostLink (ctx : Context, postObj : obj) =
        match postObj with
        | :? PostListItem as post  -> Some post.id
        | :? DropProxy    as proxy -> Option.ofObj proxy["id"] |> Option.map string
        | :? String       as theId -> Some theId
        | _ -> None
        |> function
        | Some postId -> WebLog.relativeUrl (webLog ctx) (Permalink $"admin/post/{postId}/edit")
        | None        -> $"alert('unknown post object type {postObj.GetType().Name}')"
    
/// A filter to generate nav links, highlighting the active link (exact match)
type NavLinkFilter () =
    static member NavLink (ctx : Context, url : string, text : string) =
        let webLog = webLog ctx
        seq {
            "<li class=\"nav-item\"><a class=\"nav-link"
            if url = string ctx.Environments[0].["current_page"] then " active"
            "\" href=\""
            WebLog.relativeUrl webLog (Permalink url)
            "\">"
            text
            "</a></li>"
        }
        |> Seq.fold (+) ""

/// A filter to generate a relative link
type RelativeLinkFilter () =
    static member RelativeLink (ctx : Context, item : obj) =
        permalink ctx item WebLog.relativeUrl

/// A filter to generate a link with posts tagged with the given tag
type TagLinkFilter () =
    static member TagLink (ctx : Context, tag : string) =
        ctx.Environments[0].["tag_mappings"] :?> TagMap list
        |> List.tryFind (fun it -> it.tag = tag)
        |> function
        | Some tagMap -> tagMap.urlValue
        | None        -> tag.Replace (" ", "+")
        |> function tagUrl -> WebLog.relativeUrl (webLog ctx) (Permalink $"tag/{tagUrl}/")
            
/// Create links for a user to log on or off, and a dashboard link if they are logged off
type UserLinksTag () =
    inherit Tag ()
    
    override this.Render (context : Context, result : TextWriter) =
        let webLog = webLog context
        let link it = WebLog.relativeUrl webLog (Permalink it)
        seq {
            """<ul class="navbar-nav flex-grow-1 justify-content-end">"""
            match Convert.ToBoolean context.Environments[0].["logged_on"] with
            | true ->
                $"""<li class="nav-item"><a class="nav-link" href="{link "admin"}">Dashboard</a></li>"""
                $"""<li class="nav-item"><a class="nav-link" href="{link "user/log-off"}">Log Off</a></li>"""
            | false ->
                $"""<li class="nav-item"><a class="nav-link" href="{link "user/log-on"}">Log On</a></li>"""
            "</ul>"
        }
        |> Seq.iter result.WriteLine

/// A filter to retrieve the value of a meta item from a list
//    (shorter than `{% assign item = list | where: "name", [name] | first %}{{ item.value }}`)
type ValueFilter () =
    static member Value (_ : Context, items : MetaItem list, name : string) =
        match items |> List.tryFind (fun it -> it.name = name) with
        | Some item -> item.value
        | None -> $"-- {name} not found --"


