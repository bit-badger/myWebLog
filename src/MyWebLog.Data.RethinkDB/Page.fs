module MyWebLog.Data.RethinkDB.Page

open MyWebLog.Entities
open RethinkDb.Driver.Ast

let private r = RethinkDb.Driver.RethinkDB.R

/// Try to find a page by its Id, optionally including revisions
let tryFindPageById conn webLogId (pageId : string) includeRevs =
  let pg = r.Table(Table.Page)
             .Get(pageId)
  match includeRevs with true -> pg.RunResultAsync<Page>(conn) | _ -> pg.Without("Revisions").RunResultAsync<Page>(conn)
  |> await
  |> box
  |> function
     | null -> None
     | page -> let pg : Page = unbox page
               match pg.WebLogId = webLogId with true -> Some pg | _ -> None

/// Find a page by its permalink
let tryFindPageByPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Page)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "Permalink")
    .Without("Revisions")
    .RunResultAsync<Page list>(conn)
  |> await
  |> List.tryHead

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn (webLogId : string) =
  r.Table(Table.Page)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy("Title")
    .Without("Text", "Revisions")
    .RunResultAsync<Page list>(conn)
  |> await

/// Add a page
let addPage conn (page : Page) =
  r.Table(Table.Page)
    .Insert(page)
    .RunResultAsync(conn) |> await |> ignore

type PageUpdateRecord =
  { Title : string
    Permalink : string
    PublishedOn : int64
    UpdatedOn : int64
    Text : string
    Revisions : Revision list }
/// Update a page
let updatePage conn (page : Page) =
  match tryFindPageById conn page.WebLogId page.Id false with
  | Some _ -> r.Table(Table.Page)
                .Get(page.Id)
                .Update({ PageUpdateRecord.Title = page.Title
                          Permalink = page.Permalink
                          PublishedOn = page.PublishedOn
                          UpdatedOn = page.UpdatedOn
                          Text = page.Text
                          Revisions = page.Revisions })
                .RunResultAsync(conn) |> await |> ignore
  | _ -> ()
    
/// Delete a page
let deletePage conn webLogId pageId =
  match tryFindPageById conn webLogId pageId false with
  | Some _ -> r.Table(Table.Page)
                .Get(pageId)
                .Delete()
                .RunResultAsync(conn) |> await |> ignore
  | _ -> ()
