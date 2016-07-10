module myWebLog.Data.Post

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open RethinkDb.Driver
open RethinkDb.Driver.Ast
open System.Dynamic

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

/// Try to find a post by its Id and web log Id
let tryFindPost conn webLogId postId : Post option =
  match table Table.Post
        |> get postId
        |> filter (fun p -> upcast p.["webLogId"].Eq(webLogId))
        |> runAtomAsync<Post> conn
        |> box with
  | null -> None
  | post -> Some <| unbox post

/// Try to find a post by its permalink
let tryFindPostByPermalink conn webLogId permalink =
  (table Table.Post
   |> getAll [| webLogId, permalink |]
   |> optArg "index" "permalink"
   |> without [| "revisions" |])
   .Merge(fun post -> ExpandoObject()?categories <-
                        post.["categoryIds"]
                          .Map(ReqlFunction1(fun cat -> upcast r.Table(Table.Category).Get(cat).Without("children")))
                          .CoerceTo("array"))
   .Merge(fun post -> ExpandoObject()?comments <-
                        r.Table(Table.Comment)
                          .GetAll(post.["id"]).OptArg("index", "postId")
                          .OrderBy("postedOn")
                          .CoerceTo("array"))
  |> runCursorAsync<Post> conn
  |> Seq.tryHead

/// Try to find a post by its prior permalink
let tryFindPostByPriorPermalink conn webLogId permalink =
  (table Table.Post
   |> getAll [| webLogId |]
   |> optArg "index" "webLogId")
   .Filter(fun post -> post.["priorPermalinks"].Contains(permalink :> obj))
  |> without [| "revisions" |]
  |> runCursorAsync<Post> conn
  |> Seq.tryHead

/// Save a post
let savePost conn post =
  match post.id with
  | "new" -> let newPost = { post with id = string <| System.Guid.NewGuid() }
             table Table.Post
             |> insert newPost
             |> runResultAsync conn
             |> ignore
             newPost.id
  | _     -> table Table.Post
             |> get post.id
             |> replace post
             |> runResultAsync conn
             |> ignore
             post.id

/// Count posts for a web log
let countPosts conn webLogId =
  table Table.Post
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> count
  |> runAtomAsync<int> conn
