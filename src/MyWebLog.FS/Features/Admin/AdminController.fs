namespace MyWebLog.Features.Admin

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.Rendering
open MyWebLog
open MyWebLog.Features.Shared
open RethinkDb.Driver.Net
open System.Threading.Tasks

/// Controller for admin-specific displays and routes
[<Route "/admin">]
[<Authorize>]
type AdminController () =
    inherit MyWebLogController ()

    [<HttpGet "">]
    member this.Index () = task {
        let getCount (f : WebLogId -> IConnection -> Task<int>) = f this.WebLog.id this.Db
        let! posts   = Data.Post.countByStatus Published |> getCount
        let! drafts  = Data.Post.countByStatus Draft     |> getCount
        let! pages   = Data.Page.countAll                |> getCount
        let! pages   = Data.Page.countAll                |> getCount
        let! listed  = Data.Page.countListed             |> getCount
        let! cats    = Data.Category.countAll            |> getCount
        let! topCats = Data.Category.countTopLevel       |> getCount
        return this.View (DashboardModel (
            this.WebLog,
            Posts              = posts,
            Drafts             = drafts,
            Pages              = pages,
            ListedPages        = listed,
            Categories         = cats,
            TopLevelCategories = topCats
        ))
    }

    [<HttpGet "settings">]
    member this.Settings() = task {
        let! allPages = Data.Page.findAll this.WebLog.id this.Db
        return this.View (SettingsModel (
            this.WebLog,
            DefaultPages =
                (Seq.singleton (SelectListItem ("- {Resources.FirstPageOfPosts} -", "posts"))
                 |> Seq.append (allPages |> Seq.map (fun p -> SelectListItem (p.title, PageId.toString p.id))))
        ))
    }

    [<HttpPost "settings">]
    member this.SaveSettings (model : SettingsModel) = task {
        match! Data.WebLog.findByHost this.WebLog.urlBase this.Db with
        | Some webLog ->
            let updated = model.UpdateSettings webLog
            do! Data.WebLog.updateSettings updated this.Db

            // Update cache
            WebLogCache.set (WebLogCache.hostToDb this.HttpContext) updated
        
            // TODO: confirmation message

            return this.RedirectToAction (nameof this.Index);
        | None -> return this.NotFound ()
    }
