/// JSON.NET converters for discriminated union types
[<RequireQualifiedAccess>]
module MyWebLog.JsonConverters

open MyWebLog
open Newtonsoft.Json
open System

type CategoryIdConverter () =
    inherit JsonConverter<CategoryId> ()
    override _.WriteJson (writer : JsonWriter, value : CategoryId, _ : JsonSerializer) =
        writer.WriteValue (CategoryId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : CategoryId, _ : bool, _ : JsonSerializer) =
        (string >> CategoryId) reader.Value

type CommentIdConverter () =
    inherit JsonConverter<CommentId> ()
    override _.WriteJson (writer : JsonWriter, value : CommentId, _ : JsonSerializer) =
        writer.WriteValue (CommentId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : CommentId, _ : bool, _ : JsonSerializer) =
        (string >> CommentId) reader.Value

type PermalinkConverter () =
    inherit JsonConverter<Permalink> ()
    override _.WriteJson (writer : JsonWriter, value : Permalink, _ : JsonSerializer) =
        writer.WriteValue (Permalink.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : Permalink, _ : bool, _ : JsonSerializer) =
        (string >> Permalink) reader.Value

type PageIdConverter () =
    inherit JsonConverter<PageId> ()
    override _.WriteJson (writer : JsonWriter, value : PageId, _ : JsonSerializer) =
        writer.WriteValue (PageId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : PageId, _ : bool, _ : JsonSerializer) =
        (string >> PageId) reader.Value

type PostIdConverter () =
    inherit JsonConverter<PostId> ()
    override _.WriteJson (writer : JsonWriter, value : PostId, _ : JsonSerializer) =
        writer.WriteValue (PostId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : PostId, _ : bool, _ : JsonSerializer) =
        (string >> PostId) reader.Value

type WebLogIdConverter () =
    inherit JsonConverter<WebLogId> ()
    override _.WriteJson (writer : JsonWriter, value : WebLogId, _ : JsonSerializer) =
        writer.WriteValue (WebLogId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : WebLogId, _ : bool, _ : JsonSerializer) =
        (string >> WebLogId) reader.Value

type WebLogUserIdConverter () =
    inherit JsonConverter<WebLogUserId> ()
    override _.WriteJson (writer : JsonWriter, value : WebLogUserId, _ : JsonSerializer) =
        writer.WriteValue (WebLogUserId.toString value)
    override _.ReadJson (reader : JsonReader, _ : Type, _ : WebLogUserId, _ : bool, _ : JsonSerializer) =
        (string >> WebLogUserId) reader.Value

open Microsoft.FSharpLu.Json

/// All converters to use for data conversion
let all () : JsonConverter seq =
    seq {
        // Our converters
        CategoryIdConverter   ()
        CommentIdConverter    ()
        PermalinkConverter    ()
        PageIdConverter       ()
        PostIdConverter       ()
        WebLogIdConverter     ()
        WebLogUserIdConverter ()
        // Handles DUs with no associated data, as well as option fields
        CompactUnionJsonConverter ()
    }

