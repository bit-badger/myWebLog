namespace MyWebLog

open FSharp.Markdown
open MyWebLog.Data.Page
open MyWebLog.Entities
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
    let model = PagesModel(this.Context, this.WebLog, (findAllPages conn this.WebLog.Id
                                                       |> List.map (fun p -> PageForDisplay(this.WebLog, p))))
    model.PageTitle <- Resources.Pages
    upcast this.View.["admin/page/list", model]

  /// Edit a page
  member this.EditPage (parameters : DynamicDictionary) =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match (match pageId with
           | "new" -> Some Page.Empty
           | _     -> tryFindPage conn this.WebLog.Id pageId) with
    | Some page -> let rev = match page.Revisions
                                   |> List.sortByDescending (fun r -> r.AsOf)
                                   |> List.tryHead with
                             | Some r -> r
                             | None   -> Revision.Empty
                   let model = EditPageModel(this.Context, this.WebLog, page, rev)
                   model.PageTitle <- match pageId with "new" -> Resources.AddNewPage | _ -> Resources.EditPage
                   upcast this.View.["admin/page/edit", model]
    | None      -> this.NotFound ()

  /// Save a page
  member this.SavePage (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    let form   = this.Bind<EditPageForm> ()
    let now    = clock.Now.Ticks
    match (match pageId with
           | "new" -> Some Page.Empty
           | _     -> tryFindPage conn this.WebLog.Id pageId) with
    | Some p -> let page = match pageId with "new" -> { p with WebLogId = this.WebLog.Id } | _ -> p
                let pId = { p with
                              Title       = form.Title
                              Permalink   = form.Permalink
                              PublishedOn = match pageId with "new" -> now | _ -> page.PublishedOn
                              UpdatedOn   = now
                              Text        = match form.Source with
                                            | RevisionSource.Markdown -> Markdown.TransformHtml form.Text
                                            | _                       -> form.Text
                              Revisions   = { AsOf       = now
                                              SourceType = form.Source
                                              Text       = form.Text } :: page.Revisions }
                          |> savePage conn
                let model = MyWebLogModel(this.Context, this.WebLog)
                { UserMessage.Empty with
                    Level   = Level.Info
                    Message = System.String.Format
                                (Resources.MsgPageEditSuccess,
                                 (match pageId with | "new" -> Resources.Added | _ -> Resources.Updated)) }
                |> model.AddMessage
                this.Redirect (sprintf "/page/%s/edit" pId) model
    | None   -> this.NotFound ()

  /// Delete a page
  member this.DeletePage (parameters : DynamicDictionary) =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match tryFindPageWithoutRevisions conn this.WebLog.Id pageId with
    | Some page -> deletePage conn page.WebLogId page.Id
                   let model = MyWebLogModel(this.Context, this.WebLog)
                   { UserMessage.Empty with Level   = Level.Info
                                            Message = Resources.MsgPageDeleted }
                   |> model.AddMessage
                   this.Redirect "/pages" model
    | None      -> this.NotFound ()
