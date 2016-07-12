namespace myWebLog

open myWebLog.Data.Page
open myWebLog.Entities
open Nancy
open Nancy.Security
open RethinkDb.Driver.Net

/// Handle /pages and /page URLs
type PageModule(conn : IConnection) as this =
  inherit NancyModule()

  do
    this.Get   .["/pages"           ] <- fun _     -> upcast this.PageList ()
    this.Delete.["/page/{id}/delete"] <- fun parms -> upcast this.DeletePage (downcast parms)

  /// List all pages
  member this.PageList () =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = PagesModel(this.Context, this.WebLog, findAllPages conn this.WebLog.id)
    model.pageTitle <- Resources.Pages
    this.View.["admin/page/list", model]

  // TODO: edit goes here!

  /// Delete a page
  member this.DeletePage (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId : string = downcast parameters.["id"]
    match tryFindPageWithoutRevisions conn this.WebLog.id pageId with
    | Some page -> deletePage conn page.webLogId page.id
                   let model = MyWebLogModel(this.Context, this.WebLog)
                   { level   = Level.Info
                     message = Resources.MsgPageDeleted
                     details = None }
                   |> model.addMessage
                   this.Redirect "/pages" model
    | None      -> this.NotFound ()
