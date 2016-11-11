module MyWebLog.Data.RethinkDB.Page

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Try to find a page by its Id, optionally including revisions
let tryFindPageById conn webLogId (pageId : string) includeRevs =
  async {
    let q =
      r.Table(Table.Page)
        .Get pageId
    let! thePage =
      match includeRevs with
      | true -> q.RunResultAsync<Page> conn
      | _ -> q.Without("Revisions").RunResultAsync<Page> conn
    return
      match box thePage with
      | null -> None
      | page ->
          let pg : Page = unbox page
          match pg.WebLogId = webLogId with true -> Some pg | _ -> None
    }
  |> Async.RunSynchronously

/// Find a page by its permalink
let tryFindPageByPermalink conn (webLogId : string) (permalink : string) =
  async {
    let! pg =
      r.Table(Table.Page)
        .GetAll(r.Array (webLogId, permalink)).OptArg("index", "Permalink")
        .Without("Revisions")
        .RunResultAsync<Page list> conn
    return List.tryHead pg
    }
  |> Async.RunSynchronously

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn (webLogId : string) =
  async {
    return!
      r.Table(Table.Page)
        .GetAll(webLogId).OptArg("index", "WebLogId")
        .OrderBy("Title")
        .Without("Text", "Revisions")
        .RunResultAsync<Page list> conn
    }
  |> Async.RunSynchronously

/// Add a page
let addPage conn (page : Page) =
  async {
    do! r.Table(Table.Page)
          .Insert(page)
          .RunResultAsync conn
    }
  |> (Async.RunSynchronously >> ignore)

type PageUpdateRecord =
  { Title : string
    Permalink : string
    PublishedOn : int64
    UpdatedOn : int64
    ShowInPageList : bool
    Text : string
    Revisions : Revision list }
/// Update a page
let updatePage conn (page : Page) =
  match tryFindPageById conn page.WebLogId page.Id false with
  | Some _ ->
      async {
        do! r.Table(Table.Page)
              .Get(page.Id)
              .Update({ PageUpdateRecord.Title = page.Title
                        Permalink = page.Permalink
                        PublishedOn = page.PublishedOn
                        UpdatedOn = page.UpdatedOn
                        ShowInPageList = page.ShowInPageList
                        Text = page.Text
                        Revisions = page.Revisions })
              .RunResultAsync conn
        }
      |> (Async.RunSynchronously >> ignore)
  | _ -> ()
    
/// Delete a page
let deletePage conn webLogId pageId =
  match tryFindPageById conn webLogId pageId false with
  | Some _ ->
      async {
        do! r.Table(Table.Page)
              .Get(pageId)
              .Delete()
              .RunResultAsync conn
        }
      |> (Async.RunSynchronously >> ignore)
  | _ -> ()
