module Program

open MyWebLog
open MyWebLog.Data.RethinkDB
open MyWebLog.Entities
open Nancy.Cryptography
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open NodaTime
open RethinkDb.Driver
open System
open System.Linq

let r = RethinkDB.R

let appCfg = try AppConfig.FromJson (System.IO.File.ReadAllText "config.json")
             with ex -> raise <| Exception ("Bad config.json file", ex)
let cfg = appCfg.DataConfig
// DataConfig.Connect
//            (JsonConvert.DeserializeObject<DataConfig>("""{ "hostname" : "data01", "authKey" : "1d9a76f8-2d85-4033-be15-1f4313a96bb2", "database" : "myWebLog" }"""))
let conn = cfg.Conn
let toTicks (dt : DateTime) = Instant.FromDateTimeUtc(dt.ToUniversalTime()).ToUnixTimeTicks ()
/// Hash the user's password
let pbkdf2 (pw : string) =
  PassphraseKeyGenerator(pw, appCfg.PasswordSalt, 4096).GetBytes 512
  |> Seq.fold (fun acc byt -> sprintf "%s%s" acc (byt.ToString "x2")) ""

let migr8 () =
  SetUp.startUpCheck cfg

  Console.WriteLine "Migrating web logs..."

  r.Db("MyWebLog").Table(Table.WebLog)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.WebLog)
        .Insert({ Id          = string x.["id"]
                  Name        = string x.["name"]
                  Subtitle    = Some <| string x.["subtitle"]
                  DefaultPage = string x.["defaultPage"]
                  ThemePath   = string x.["themePath"]
                  TimeZone    = string x.["timeZone"]
                  UrlBase     = string x.["urlBase"]
                  PageList    = []
                  })
        .RunResult(conn)
      |> ignore)

  Console.WriteLine "Migrating users..."

  r.Db("MyWebLog").Table(Table.User)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.User)
        .Insert({ Id             = string x.["id"]
                  UserName       = string x.["userName"]
                  FirstName      = string x.["firstName"]
                  LastName       = string x.["lastName"]
                  PreferredName  = string x.["preferredName"]
                  PasswordHash   = string x.["passwordHash"]
                  Url            = Some <| string x.["url"]
                  Authorizations = x.["authorizations"] :?> JArray
                                   |> Seq.map (fun y -> { WebLogId = string y.["webLogId"]
                                                          Level    = string y.["level"] })
                                   |> Seq.toList
                  })
        .RunResult(conn)
      |> ignore)

  Console.WriteLine "Migrating categories..."

  r.Db("MyWebLog").Table(Table.Category)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.Category)
        .Insert({ Id          = string x.["id"]
                  WebLogId    = string x.["webLogId"]
                  Name        = string x.["name"]
                  Slug        = string x.["slug"]
                  Description = match String.IsNullOrEmpty(string x.["description"]) with
                                | true -> None
                                | _    -> Some <| string x.["description"]
                  ParentId    = match String.IsNullOrEmpty(string x.["parentId"]) with
                                | true -> None
                                | _    -> Some <| string x.["parentId"]
                  Children    = x.["children"] :?> JArray
                                |> Seq.map (fun y -> string y)
                                |> Seq.toList
                  })
        .RunResult(conn)
      |> ignore)
                        
  Console.WriteLine "Migrating comments..."

  r.Db("MyWebLog").Table(Table.Comment)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.Comment)
        .Insert({ Id          = string x.["id"]
                  PostId      = string x.["postId"]
                  InReplyToId = match String.IsNullOrEmpty(string x.["inReplyToId"]) with
                                | true -> None
                                | _    -> Some <| string x.["inReplyToId"]
                  Name        = string x.["name"]
                  Email       = string x.["email"]
                  Url         = match String.IsNullOrEmpty(string x.["url"]) with
                                | true -> None
                                | _    -> Some <| string x.["url"]
                  Status      = string x.["status"]
                  PostedOn    = x.["postedDate"].ToObject<DateTime>() |> toTicks
                  Text        = string x.["text"]
                  })
        .RunResult(conn)
      |> ignore)

  Console.WriteLine "Migrating pages..."

  r.Db("MyWebLog").Table(Table.Page)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.Page)
        .Insert({ Id             = string x.["id"]
                  WebLogId       = string x.["webLogId"]
                  AuthorId       = string x.["authorId"]
                  Title          = string x.["title"]
                  Permalink      = string x.["permalink"]
                  PublishedOn    = x.["publishedDate"].ToObject<DateTime> () |> toTicks
                  UpdatedOn      = x.["lastUpdatedDate"].ToObject<DateTime> () |> toTicks
                  ShowInPageList = x.["showInPageList"].ToObject<bool>()
                  Text           = string x.["text"]
                  Revisions      = [{ AsOf = x.["lastUpdatedDate"].ToObject<DateTime> () |> toTicks
                                      SourceType = RevisionSource.HTML
                                      Text       = string x.["text"]
                                      }]
                  })
        .RunResult(conn)
      |> ignore)

  Console.WriteLine "Migrating posts..."

  r.Db("MyWebLog").Table(Table.Post)
    .RunCursor<JObject>(conn)
  |> Seq.iter (fun x ->
      r.Db("myWebLog").Table(Table.Post)
        .Insert({ Id              = string x.["id"]
                  WebLogId        = string x.["webLogId"]
                  AuthorId        = "9b491a0f-48df-4b7b-8c10-120b5cd02895"
                  Status          = string x.["status"]
                  Title           = string x.["title"]
                  Permalink       = string x.["permalink"]
                  PublishedOn     = match x.["publishedDate"] with
                                    | null -> int64 0
                                    | dt   -> dt.ToObject<DateTime> () |> toTicks
                  UpdatedOn       = x.["lastUpdatedDate"].ToObject<DateTime> () |> toTicks
                  Revisions       = [{ AsOf       = x.["lastUpdatedDate"].ToObject<DateTime> ()
                                                    |> toTicks
                                       SourceType = RevisionSource.HTML
                                       Text       = string x.["text"]
                                       }]
                  Text            = string x.["text"]
                  Tags            = x.["tag"] :?> JArray
                                    |> Seq.map (fun y -> string y)
                                    |> Seq.toList
                  CategoryIds     = x.["category"] :?> JArray
                                    |> Seq.map (fun y -> string y)
                                    |> Seq.toList
                  PriorPermalinks = []
                  Categories      = []
                  Comments        = []
                  })
        .RunResult(conn)
      |> ignore)
                      

[<EntryPoint>]
let main argv = 
    migr8 ()
    0 // return an integer exit code
