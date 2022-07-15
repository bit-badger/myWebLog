/// Custom DotLiquid filters and tags
module MyWebLog.DotLiquidBespoke

open System
open System.IO
open System.Web
open DotLiquid
open Giraffe.ViewEngine
open MyWebLog.ViewModels

/// Get the current web log from the DotLiquid context
let webLog (ctx : Context) =
    ctx.Environments[0].["web_log"] :?> WebLog

/// Does an asset exist for the current theme?
let assetExists fileName (webLog : WebLog) =
    ThemeAssetCache.get (ThemeId webLog.themePath) |> List.exists (fun it -> it = fileName)

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
        let _, path = WebLog.hostAndPath webLog
        let path = if path = "" then path else $"{path.Substring 1}/"
        seq {
            "<li class=\"nav-item\"><a class=\"nav-link"
            if (string ctx.Environments[0].["current_page"]).StartsWith $"{path}{url}" then " active"
            "\" href=\""
            WebLog.relativeUrl webLog (Permalink url)
            "\">"
            text
            "</a></li>"
        }
        |> Seq.fold (+) ""


/// A filter to generate a link for theme asset (image, stylesheet, script, etc.)
type ThemeAssetFilter () =
    static member ThemeAsset (ctx : Context, asset : string) =
        let webLog = webLog ctx
        WebLog.relativeUrl webLog (Permalink $"themes/{webLog.themePath}/{asset}")


/// Create various items in the page header based on the state of the page being generated
type PageHeadTag () =
    inherit Tag ()
    
    override this.Render (context : Context, result : TextWriter) =
        let webLog = webLog context
        // spacer
        let s      = "    "
        let getBool name =
            context.Environments[0].[name] |> Option.ofObj |> Option.map Convert.ToBoolean |> Option.defaultValue false
        
        result.WriteLine $"""<meta name="generator" content="{context.Environments[0].["generator"]}">"""
        
        // Theme assets
        if assetExists "style.css" webLog then
            result.WriteLine $"""{s}<link rel="stylesheet" href="{ThemeAssetFilter.ThemeAsset (context, "style.css")}">"""
        if assetExists "favicon.ico" webLog then
            result.WriteLine $"""{s}<link rel="icon" href="{ThemeAssetFilter.ThemeAsset (context, "favicon.ico")}">"""
        
        // RSS feeds and canonical URLs
        let feedLink title url =
            let escTitle = HttpUtility.HtmlAttributeEncode title
            let relUrl   = WebLog.relativeUrl webLog (Permalink url)
            $"""{s}<link rel="alternate" type="application/rss+xml" title="{escTitle}" href="{relUrl}">"""
        
        if webLog.rss.feedEnabled && getBool "is_home" then
            result.WriteLine (feedLink webLog.name webLog.rss.feedName)
            result.WriteLine $"""{s}<link rel="canonical" href="{WebLog.absoluteUrl webLog Permalink.empty}">"""
        
        if webLog.rss.categoryEnabled && getBool "is_category_home" then
            let slug = context.Environments[0].["slug"] :?> string
            result.WriteLine (feedLink webLog.name $"category/{slug}/{webLog.rss.feedName}")
            
        if webLog.rss.tagEnabled && getBool "is_tag_home" then
            let slug = context.Environments[0].["slug"] :?> string
            result.WriteLine (feedLink webLog.name $"tag/{slug}/{webLog.rss.feedName}")
            
        if getBool "is_post" then
            let post = context.Environments[0].["model"] :?> PostDisplay
            let url  = WebLog.absoluteUrl webLog (Permalink post.posts[0].permalink)
            result.WriteLine $"""{s}<link rel="canonical" href="{url}">"""
        
        if getBool "is_page" then
            let page = context.Environments[0].["page"] :?> DisplayPage
            let url  = WebLog.absoluteUrl webLog (Permalink page.permalink)
            result.WriteLine $"""{s}<link rel="canonical" href="{url}">"""


/// Create various items in the page header based on the state of the page being generated
type PageFootTag () =
    inherit Tag ()
    
    override this.Render (context : Context, result : TextWriter) =
        let webLog = webLog context
        // spacer
        let s = "    "
        
        if webLog.autoHtmx then
            result.WriteLine $"{s}{RenderView.AsString.htmlNode Htmx.Script.minified}"
        
        if assetExists "script.js" webLog then
            result.WriteLine $"""{s}<script src="{ThemeAssetFilter.ThemeAsset (context, "script.js")}"></script>"""

        
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
                $"""<li class="nav-item"><a class="nav-link" href="{link "admin/dashboard"}">Dashboard</a></li>"""
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


open System.Collections.Generic
open Microsoft.AspNetCore.Antiforgery

/// Register custom filters/tags and safe types
let register () =
    [ typeof<AbsoluteLinkFilter>; typeof<CategoryLinkFilter>; typeof<EditPageLinkFilter>; typeof<EditPostLinkFilter>
      typeof<NavLinkFilter>;      typeof<RelativeLinkFilter>; typeof<TagLinkFilter>;      typeof<ThemeAssetFilter>
      typeof<ValueFilter>
    ]
    |> List.iter Template.RegisterFilter
    
    Template.RegisterTag<PageHeadTag>  "page_head"
    Template.RegisterTag<PageFootTag>  "page_foot"
    Template.RegisterTag<UserLinksTag> "user_links"
    
    [ // Domain types
      typeof<CustomFeed>; typeof<Episode>; typeof<Episode option>;    typeof<MetaItem>; typeof<Page>
      typeof<RssOptions>; typeof<TagMap>;  typeof<UploadDestination>; typeof<WebLog>
      // View models
      typeof<DashboardModel>;  typeof<DisplayCategory>;  typeof<DisplayCustomFeed>;     typeof<DisplayPage>
      typeof<DisplayRevision>; typeof<DisplayUpload>;    typeof<EditCategoryModel>;     typeof<EditCustomFeedModel>
      typeof<EditPageModel>;   typeof<EditPostModel>;    typeof<EditRssModel>;          typeof<EditTagMapModel>
      typeof<EditUserModel>;   typeof<LogOnModel>;       typeof<ManagePermalinksModel>; typeof<ManageRevisionsModel>
      typeof<PostDisplay>;     typeof<PostListItem>;     typeof<SettingsModel>;         typeof<UserMessage>
      // Framework types
      typeof<AntiforgeryTokenSet>; typeof<DateTime option>; typeof<int option>;    typeof<KeyValuePair>
      typeof<MetaItem list>;       typeof<string list>;     typeof<string option>; typeof<TagMap list>
    ]
    |> List.iter (fun it -> Template.RegisterSafeType (it, [| "*" |]))
