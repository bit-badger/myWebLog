namespace myWebLog

open FSharp.Markdown
open myWebLog.Data.Page
open myWebLog.Entities
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open NodaTime
open RethinkDb.Driver.Net

/// Handle /pages and /page URLs
type PageModule(conn : IConnection, clock : IClock) as this =
  inherit NancyModule()

  do
    this.Get   .["/pages"           ] <- fun _     -> this.PageList ()
    this.Get   .["/page/{id}/edit"  ] <- fun parms -> this.EditPage   (downcast parms)
    this.Post  .["/page/{id}/edit"  ] <- fun parms -> this.SavePage   (downcast parms)
    this.Delete.["/page/{id}/delete"] <- fun parms -> this.DeletePage (downcast parms)

  /// List all pages
  member this.PageList () =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model = PagesModel(this.Context, this.WebLog, findAllPages conn this.WebLog.id)
    model.pageTitle <- Resources.Pages
    upcast this.View.["admin/page/list", model]

  /// Edit a page
  member this.EditPage (parameters : DynamicDictionary) =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match (match pageId with
           | "new" -> Some Page.empty
           | _     -> tryFindPage conn this.WebLog.id pageId) with
    | Some page -> let rev = match page.revisions
                                   |> List.sortByDescending (fun r -> r.asOf)
                                   |> List.tryHead with
                             | Some r -> r
                             | None   -> Revision.empty
                   let model = EditPageModel(this.Context, this.WebLog, page, rev)
                   model.pageTitle <- match pageId with
                                      | "new" -> Resources.AddNewPage
                                      | _     -> Resources.EditPage
                   upcast this.View.["admin/page/edit"]
    | None      -> this.NotFound ()

  /// Save a page
  member this.SavePage (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    let form   = this.Bind<EditPageForm> ()
    let now    = clock.Now.Ticks
    match (match pageId with
           | "new" -> Some Page.empty
           | _     -> tryFindPage conn this.WebLog.id pageId) with
    | Some p -> let page = match pageId with
                           | "new" -> { p with webLogId = this.WebLog.id }
                           | _     -> p
                let pId = { p with
                              title       = form.title
                              permalink   = form.permalink
                              publishedOn = match pageId with | "new" -> now | _ -> page.publishedOn
                              updatedOn   = now
                              text        = match form.source with
                                            | RevisionSource.Markdown -> Markdown.TransformHtml form.text
                                            | _                       -> form.text
                              revisions   = { asOf       = now
                                              sourceType = form.source
                                              text       = form.text } :: page.revisions }
                          |> savePage conn
                let model = MyWebLogModel(this.Context, this.WebLog)
                { level   = Level.Info
                  message = System.String.Format
                              (Resources.MsgPageEditSuccess,
                               (match pageId with | "new" -> Resources.Added | _ -> Resources.Updated))
                  details = None }
                |> model.addMessage
                this.Redirect (sprintf "/page/%s/edit" pId) model
    | None   -> this.NotFound ()

  /// Delete a page
  member this.DeletePage (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match tryFindPageWithoutRevisions conn this.WebLog.id pageId with
    | Some page -> deletePage conn page.webLogId page.id
                   let model = MyWebLogModel(this.Context, this.WebLog)
                   { level   = Level.Info
                     message = Resources.MsgPageDeleted
                     details = None }
                   |> model.addMessage
                   this.Redirect "/pages" model
    | None      -> this.NotFound ()
