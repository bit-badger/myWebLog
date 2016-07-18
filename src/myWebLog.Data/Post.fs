module myWebLog.Data.Post

open FSharp.Interop.Dynamic
open myWebLog.Entities
open Rethink
open RethinkDb.Driver
open RethinkDb.Driver.Ast
open System.Dynamic

let private r = RethinkDB.R

/// Shorthand to select all published posts for a web log
let private publishedPosts webLogId =
  table Table.Post
  |> getAll [| webLogId; PostStatus.Published |]
  |> optArg "index" "webLogAndStatus"

/// Shorthand to sort posts by published date, slice for the given page, and return a list
let private toPostList conn pageNbr nbrPerPage filter =
  filter
  |> orderBy (fun p -> upcast r.Desc(p.["publishedOn"]))
  |> slice ((pageNbr - 1) * nbrPerPage) (pageNbr * nbrPerPage)
  |> runAtomAsync<System.Collections.Generic.List<Post>> conn
  |> Seq.toList

/// Shorthand to get a newer or older post
let private adjacentPost conn post theFilter =
  System.Console.WriteLine "Adjacent post"
  publishedPosts post.webLogId
  |> filter theFilter
  |> orderBy (fun p -> upcast p.["publishedOn"])
  |> limit 1
  |> runCursorAsync<Post> conn
  |> Seq.tryHead

/// Get a page of published posts
let findPageOfPublishedPosts conn webLogId pageNbr nbrPerPage =
  publishedPosts webLogId
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts assigned to a given category
let findPageOfCategorizedPosts conn webLogId (categoryId : string) pageNbr nbrPerPage =
  publishedPosts webLogId
  |> filter  (fun p -> upcast p.["categoryIds"].Contains(categoryId))
  |> toPostList conn pageNbr nbrPerPage

/// Get a page of published posts tagged with a given tag
let findPageOfTaggedPosts conn webLogId (tag : string) pageNbr nbrPerPage =
  publishedPosts webLogId
  |> filter (fun p -> upcast p.["tags"].Contains(tag))
  |> toPostList conn pageNbr nbrPerPage

/// Try to get the next newest post from the given post
let tryFindNewerPost conn post = adjacentPost conn post (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn))
 
/// Try to get the next newest post assigned to the given category
let tryFindNewerCategorizedPost conn (categoryId : string) post =
  adjacentPost conn post
               (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn).And(p.["categoryIds"].Contains(categoryId)))
 
/// Try to get the next newest tagged post from the given tagged post
let tryFindNewerTaggedPost conn (tag : string) post =
  adjacentPost conn post (fun p -> upcast p.["publishedOn"].Gt(post.publishedOn).And(p.["tags"].Contains(tag)))
 
/// Try to get the next oldest post from the given post
let tryFindOlderPost conn post = adjacentPost conn post (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn))

/// Try to get the next oldest post assigned to the given category
let tryFindOlderCategorizedPost conn (categoryId : string) post =
  adjacentPost conn post
               (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn).And(p.["categoryIds"].Contains(categoryId)))

/// Try to get the next oldest tagged post from the given tagged post
let tryFindOlderTaggedPost conn (tag : string) post =
  adjacentPost conn post (fun p -> upcast p.["publishedOn"].Lt(post.publishedOn).And(p.["tags"].Contains(tag)))

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
   |> getAll [| webLogId; permalink |]
   |> optArg "index" "permalink"
   |> filter (fun p -> upcast p.["status"].Eq(PostStatus.Published))
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
let tryFindPostByPriorPermalink conn webLogId (permalink : string) =
  table Table.Post
  |> getAll [| webLogId |]
  |> optArg "index" "webLogId"
  |> filter (fun p -> upcast p.["priorPermalinks"].Contains(permalink).And(p.["status"].Eq(PostStatus.Published)))
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
