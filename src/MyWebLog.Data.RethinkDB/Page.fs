module MyWebLog.Data.RethinkDB.Page

open FSharp.Interop.Dynamic
open MyWebLog.Entities
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDb.Driver.RethinkDB.R

/// Try to find a page by its Id, optionally including revisions
let tryFindPageById conn webLogId (pageId : string) includeRevs =
  let pg = r.Table(Table.Page)
             .Get(pageId)
  match (match includeRevs with
         | true -> pg.RunAtomAsync<Page>(conn)
         | _ -> pg.Without("Revisions").RunAtomAsync<Page>(conn)
         |> await |> box) with
  | null -> None
  | page -> let pg : Page = unbox page
            match pg.WebLogId = webLogId with true -> Some pg | _ -> None

/// Find a page by its permalink
let tryFindPageByPermalink conn (webLogId : string) (permalink : string) =
  r.Table(Table.Page)
    .GetAll(r.Array(webLogId, permalink)).OptArg("index", "Permalink")
    .Without("Revisions")
    .RunCursorAsync<Page>(conn)
  |> await
  |> Seq.tryHead

/// Get a list of all pages (excludes page text and revisions)
let findAllPages conn (webLogId : string) =
  r.Table(Table.Page)
    .GetAll(webLogId).OptArg("index", "WebLogId")
    .OrderBy("Title")
    .Without("Text", "Revisions")
    .RunListAsync<Page>(conn)
  |> await
  |> Seq.toList

/// Add a page
let addPage conn (page : Page) =
  r.Table(Table.Page)
    .Insert(page)
    .RunResultAsync(conn) |> await |> ignore

/// Update a page
let updatePage conn (page : Page) =
  match tryFindPageById conn page.WebLogId page.Id false with
  | Some _ -> let upd8 = ExpandoObject()
              upd8?Title       <- page.Title
              upd8?Permalink   <- page.Permalink
              upd8?PublishedOn <- page.PublishedOn
              upd8?UpdatedOn   <- page.UpdatedOn
              upd8?Text        <- page.Text
              upd8?Revisions   <- page.Revisions
              r.Table(Table.Page)
                .Get(page.Id)
                .Update(upd8)
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
