namespace MyWebLog

open MyWebLog.Data
open MyWebLog.Entities
open MyWebLog.Logic.Page
open MyWebLog.Resources
open Nancy
open Nancy.ModelBinding
open Nancy.Security
open NodaTime
open RethinkDb.Driver.Net

/// Handle /pages and /page URLs
type PageModule (data : IMyWebLogData, clock : IClock) as this =
  inherit NancyModule ()

  do
    this.Get    ("/pages",            fun _     -> this.PageList   ())
    this.Get    ("/page/{id}/edit",   fun parms -> this.EditPage   (downcast parms))
    this.Post   ("/page/{id}/edit",   fun parms -> this.SavePage   (downcast parms))
    this.Delete ("/page/{id}/delete", fun parms -> this.DeletePage (downcast parms))

  /// List all pages
  member this.PageList () : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let model =
      PagesModel(this.Context, this.WebLog, findAllPages data this.WebLog.Id
                                            |> List.map (fun p -> PageForDisplay (this.WebLog, p)))
    model.PageTitle <- Strings.get "Pages"
    upcast this.View.["admin/page/list", model]

  /// Edit a page
  member this.EditPage (parameters : DynamicDictionary) : obj =
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match pageId with "new" -> Some Page.Empty | _ -> tryFindPage data this.WebLog.Id pageId
    |> function
    | Some page ->
        let rev = match page.Revisions
                        |> List.sortByDescending (fun r -> r.AsOf)
                        |> List.tryHead with
                  | Some r -> r
                  | _ -> Revision.Empty
        let model = EditPageModel (this.Context, this.WebLog, page, rev)
        model.PageTitle <- Strings.get <| match pageId with "new" -> "AddNewPage" | _ -> "EditPage"
        upcast this.View.["admin/page/edit", model]
    | _ -> this.NotFound ()

  /// Save a page
  member this.SavePage (parameters : DynamicDictionary) : obj =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    let form   = this.Bind<EditPageForm> ()
    let now    = clock.GetCurrentInstant().ToUnixTimeTicks ()
    match pageId with "new" -> Some Page.Empty | _ -> tryFindPage data this.WebLog.Id pageId 
    |> function
    | Some p ->
        let page = match pageId with "new" -> { p with WebLogId = this.WebLog.Id } | _ -> p
        let pId =
          { p with
              Title          = form.Title
              Permalink      = form.Permalink
              PublishedOn    = match pageId with "new" -> now | _ -> page.PublishedOn
              UpdatedOn      = now
              ShowInPageList = form.ShowInPageList
              Text           = match form.Source with
                               | RevisionSource.Markdown -> (* Markdown.TransformHtml *) form.Text
                               | _ -> form.Text
              Revisions      = { AsOf       = now
                                 SourceType = form.Source
                                 Text       = form.Text
                                 } :: page.Revisions
            }
          |> savePage data
        let model = MyWebLogModel (this.Context, this.WebLog)
        { UserMessage.Empty with
            Level   = Level.Info
            Message = System.String.Format
                        (Strings.get "MsgPageEditSuccess",
                          Strings.get (match pageId with "new" -> "Added" | _ -> "Updated")) }
        |> model.AddMessage
        this.Redirect (sprintf "/page/%s/edit" pId) model
    | _ -> this.NotFound ()

  /// Delete a page
  member this.DeletePage (parameters : DynamicDictionary) : obj =
    this.ValidateCsrfToken ()
    this.RequiresAccessLevel AuthorizationLevel.Administrator
    let pageId = parameters.["id"].ToString ()
    match tryFindPageWithoutRevisions data this.WebLog.Id pageId with
    | Some page ->
        deletePage data page.WebLogId page.Id
        let model = MyWebLogModel (this.Context, this.WebLog)
        { UserMessage.Empty with
            Level   = Level.Info
            Message = Strings.get "MsgPageDeleted" }
        |> model.AddMessage
        this.Redirect "/pages" model
    | _ -> this.NotFound ()
