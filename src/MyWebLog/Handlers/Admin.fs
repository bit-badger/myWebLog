/// Handlers to manipulate admin functions
module MyWebLog.Handlers.Admin

open System.Collections.Generic
open System.IO

/// The currently available themes
let private themes () =
    Directory.EnumerateDirectories "themes"
    |> Seq.map (fun it -> it.Split Path.DirectorySeparatorChar |> Array.last)
    |> Seq.filter (fun it -> it <> "admin")
    |> Seq.map (fun it -> KeyValuePair.Create (it, it))
    |> Array.ofSeq

open System.Threading.Tasks
open DotLiquid
open Giraffe
open MyWebLog
open MyWebLog.ViewModels
open RethinkDb.Driver.Net

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

