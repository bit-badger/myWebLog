/// Custom DotLiquid filters and tags
module MyWebLog.DotLiquidBespoke

open System
open System.IO
open System.Web
open DotLiquid
open Giraffe.ViewEngine
open MyWebLog.ViewModels
open MyWebLog.Views

/// Extensions on the DotLiquid Context object
type Context with
    
    /// Get the current web log from the DotLiquid context
    member this.WebLog =
        this.Environments[0].["web_log"] :?> WebLog


/// Does an asset exist for the current theme?
let assetExists fileName (webLog: WebLog) =
    ThemeAssetCache.get webLog.ThemeId |> List.exists (fun it -> it = fileName)

/// Obtain the link from known types
let permalink (item: obj) (linkFunc: Permalink -> string) =
    match item with
    | :? String       as link  -> Some link
    | :? DisplayPage  as page  -> Some page.Permalink
    | :? PostListItem as post  -> Some post.Permalink
    | :? DropProxy    as proxy -> Option.ofObj proxy["Permalink"] |> Option.map string
    | _ -> None
    |> function
    | Some link -> linkFunc (Permalink link)
    | None      -> $"alert('unknown item type {item.GetType().Name}')"


/// A filter to generate an absolute link
type AbsoluteLinkFilter() =
    static member AbsoluteLink(ctx: Context, item: obj) =
        permalink item ctx.WebLog.AbsoluteUrl


/// A filter to generate a link with posts categorized under the given category
type CategoryLinkFilter() =
    static member CategoryLink(ctx: Context, catObj: obj) =
        match catObj with
        | :? DisplayCategory as cat   -> Some cat.Slug
        | :? DropProxy       as proxy -> Option.ofObj proxy["Slug"] |> Option.map string
        | _ -> None
        |> function
        | Some slug -> ctx.WebLog.RelativeUrl(Permalink $"category/{slug}/")
        | None      -> $"alert('unknown category object type {catObj.GetType().Name}')"


/// A filter to generate a link that will edit a page
type EditPageLinkFilter() =
    static member EditPageLink(ctx: Context, pageObj: obj) =
        match pageObj with
        | :? DisplayPage as page  -> Some page.Id
        | :? DropProxy   as proxy -> Option.ofObj proxy["Id"] |> Option.map string
        | :? String      as theId -> Some theId
        | _ -> None
        |> function
        | Some pageId -> ctx.WebLog.RelativeUrl(Permalink $"admin/page/{pageId}/edit")
        | None        -> $"alert('unknown page object type {pageObj.GetType().Name}')"


/// A filter to generate a link that will edit a post
type EditPostLinkFilter() =
    static member EditPostLink(ctx: Context, postObj: obj) =
        match postObj with
        | :? PostListItem as post  -> Some post.Id
        | :? DropProxy    as proxy -> Option.ofObj proxy["Id"] |> Option.map string
        | :? String       as theId -> Some theId
        | _ -> None
        |> function
        | Some postId -> ctx.WebLog.RelativeUrl(Permalink $"admin/post/{postId}/edit")
        | None        -> $"alert('unknown post object type {postObj.GetType().Name}')"
 
    
/// A filter to generate nav links, highlighting the active link (exact match)
type NavLinkFilter() =
    static member NavLink(ctx: Context, url: string, text: string) =
        let extraPath = ctx.WebLog.ExtraPath
        let path = if extraPath = "" then "" else $"{extraPath[1..]}/"
        seq {
            "<li class=nav-item><a class=\"nav-link"
            if (string ctx.Environments[0].["current_page"]).StartsWith $"{path}{url}" then " active"
            "\" href=\""
            ctx.WebLog.RelativeUrl(Permalink url)
            "\">"
            text
            "</a>"
        }
        |> String.concat ""


/// A filter to generate a link for theme asset (image, stylesheet, script, etc.)
type ThemeAssetFilter() =
    static member ThemeAsset(ctx: Context, asset: string) =
        ctx.WebLog.RelativeUrl(Permalink $"themes/{ctx.WebLog.ThemeId}/{asset}")


/// Create various items in the page header based on the state of the page being generated
type PageHeadTag() =
    inherit Tag()
    
    override this.Render(context: Context, result: TextWriter) =
        let webLog = context.WebLog
        // spacer
        let s      = "    "
        let getBool name =
            defaultArg (context.Environments[0].[name] |> Option.ofObj |> Option.map Convert.ToBoolean) false
        
        result.WriteLine $"""<meta name=generator content="{context.Environments[0].["generator"]}">"""
        
        // Theme assets
        if assetExists "style.css" webLog then
            result.WriteLine $"""{s}<link rel=stylesheet href="{ThemeAssetFilter.ThemeAsset(context, "style.css")}">"""
        if assetExists "favicon.ico" webLog then
            result.WriteLine $"""{s}<link rel=icon href="{ThemeAssetFilter.ThemeAsset(context, "favicon.ico")}">"""
        
        // RSS feeds and canonical URLs
        let feedLink title url =
            let escTitle = HttpUtility.HtmlAttributeEncode title
            let relUrl   = webLog.RelativeUrl(Permalink url)
            $"""{s}<link rel=alternate type="application/rss+xml" title="{escTitle}" href="{relUrl}">"""
        
        if webLog.Rss.IsFeedEnabled && getBool "is_home" then
            result.WriteLine(feedLink webLog.Name webLog.Rss.FeedName)
            result.WriteLine $"""{s}<link rel=canonical href="{webLog.AbsoluteUrl Permalink.Empty}">"""
        
        if webLog.Rss.IsCategoryEnabled && getBool "is_category_home" then
            let slug = context.Environments[0].["slug"] :?> string
            result.WriteLine(feedLink webLog.Name $"category/{slug}/{webLog.Rss.FeedName}")
            
        if webLog.Rss.IsTagEnabled && getBool "is_tag_home" then
            let slug = context.Environments[0].["slug"] :?> string
            result.WriteLine(feedLink webLog.Name $"tag/{slug}/{webLog.Rss.FeedName}")
            
        if getBool "is_post" then
            let post = context.Environments[0].["model"] :?> PostDisplay
            let url  = webLog.AbsoluteUrl (Permalink post.Posts[0].Permalink)
            result.WriteLine $"""{s}<link rel=canonical href="{url}">"""
        
        if getBool "is_page" then
            let page = context.Environments[0].["page"] :?> DisplayPage
            let url  = webLog.AbsoluteUrl (Permalink page.Permalink)
            result.WriteLine $"""{s}<link rel=canonical href="{url}">"""


/// Create various items in the page header based on the state of the page being generated
type PageFootTag() =
    inherit Tag()
    
    override this.Render(context: Context, result: TextWriter) =
        let webLog = context.WebLog
        // spacer
        let s = "    "
        
        if webLog.AutoHtmx then
            result.WriteLine $"{s}{RenderView.AsString.htmlNode Htmx.Script.minified}"
        
        if assetExists "script.js" webLog then
            result.WriteLine $"""{s}<script src="{ThemeAssetFilter.ThemeAsset(context, "script.js")}"></script>"""


/// A filter to generate a relative link
type RelativeLinkFilter() =
    static member RelativeLink(ctx: Context, item: obj) =
        permalink item ctx.WebLog.RelativeUrl


/// A filter to generate a link with posts tagged with the given tag
type TagLinkFilter() =
    static member TagLink(ctx: Context, tag: string) =
        ctx.Environments[0].["tag_mappings"] :?> TagMap list
        |> List.tryFind (fun it -> it.Tag = tag)
        |> function
        | Some tagMap -> tagMap.UrlValue
        | None        -> tag.Replace(" ", "+")
        |> function tagUrl -> ctx.WebLog.RelativeUrl(Permalink $"tag/{tagUrl}/")


/// Create links for a user to log on or off, and a dashboard link if they are logged off
type UserLinksTag() =
    inherit Tag()
    
    override this.Render(context: Context, result: TextWriter) =
        let link it = context.WebLog.RelativeUrl(Permalink it)
        seq {
            """<ul class="navbar-nav flex-grow-1 justify-content-end">"""
            match Convert.ToBoolean context.Environments[0].["is_logged_on"] with
            | true ->
                $"""<li class=nav-item><a class=nav-link href="{link "admin/dashboard"}">Dashboard</a>"""
                $"""<li class=nav-item><a class=nav-link href="{link "user/log-off"}">Log Off</a>"""
            | false ->
                $"""<li class=nav-item><a class=nav-link href="{link "user/log-on"}">Log On</a>"""
            "</ul>"
        }
        |> Seq.iter result.WriteLine

/// A filter to retrieve the value of a meta item from a list
//    (shorter than `{% assign item = list | where: "Name", [name] | first %}{{ item.value }}`)
type ValueFilter() =
    static member Value(_: Context, items: MetaItem list, name: string) =
        match items |> List.tryFind (fun it -> it.Name = name) with
        | Some item -> item.Value
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
      typeof<CustomFeed>;   typeof<Episode>;    typeof<Episode option>; typeof<MetaItem>;          typeof<Page>
      typeof<RedirectRule>; typeof<RssOptions>; typeof<TagMap>;         typeof<UploadDestination>; typeof<WebLog>
      // View models
      typeof<AppViewContext>;      typeof<DisplayCategory>; typeof<DisplayCustomFeed>; typeof<DisplayPage>
      typeof<DisplayTheme>;        typeof<DisplayUpload>;   typeof<DisplayUser>;       typeof<EditCategoryModel>
      typeof<EditCustomFeedModel>; typeof<EditPageModel>;   typeof<EditPostModel>;     typeof<EditRssModel>
      typeof<PostDisplay>;         typeof<PostListItem>;    typeof<SettingsModel>;     typeof<UserMessage>
      // Framework types
      typeof<AntiforgeryTokenSet>; typeof<DateTime option>; typeof<int option>;    typeof<KeyValuePair>
      typeof<MetaItem list>;       typeof<string list>;     typeof<string option>; typeof<TagMap list>
    ]
    |> List.iter (fun it -> Template.RegisterSafeType (it, [| "*" |]))
