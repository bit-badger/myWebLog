module myWebLog.Data.Post

open myWebLog.Entities
open Rethink
open RethinkDb.Driver

let private r = RethinkDB.R

/// Get a page of published posts
let findPageOfPublishedPosts conn webLogId pageNbr nbrPerPage =
  table Table.Post
  |> getAll [| webLogId, PostStatus.Published |]
  |> optArg "index" "webLogAndStatus"
  |> orderBy (fun p -> upcast r.Desc(p.["publishedOn"]))
  |> slice ((pageNbr - 1) * nbrPerPage) (pageNbr * nbrPerPage)
  |> runCursorAsync<Post> conn
  |> Seq.toList

/// Try to get the next newest post from the given post
let tryFindNewerPost conn post =
  table Table.Post
  |> getAll [| post.webLogId, PostStatus.Published |]
  |> optArg "index" "webLogAndStatus"
  |> filter  (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn))
  |> orderBy (fun p -> upcast p.["publishedOn"])
  |> limit 1
  |> runCursorAsync<Post> conn
  |> Seq.tryHead
 
/// Try to get the next oldest post from the given post
let tryFindOlderPost conn post =
  table Table.Post
  |> getAll [| post.webLogId, PostStatus.Published |]
  |> optArg "index" "webLogAndStatus"
  |> filter  (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn))
  |> orderBy (fun p -> upcast r.Desc(p.["publishedOn"]))
  |> limit 1
  |> runCursorAsync<Post> conn
  |> Seq.tryHead

/// Get a page of all posts in all statuses
let findPageOfAllPosts conn webLogId pageNbr nbrPerPage =
  table Table.Post
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> orderBy (fun p -> upcast r.Desc(r.Branch(p.["publishedOn"].Eq(int64 0), p.["lastUpdatedOn"], p.["publishedOn"])))
  |> slice ((pageNbr - 1) * nbrPerPage) (pageNbr * nbrPerPage)
  |> runCursorAsync<Post> conn
  |> Seq.toList
