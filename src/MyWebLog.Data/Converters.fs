/// Converters for discriminated union types
module MyWebLog.Converters

open MyWebLog
open System

/// JSON.NET converters for discriminated union types
module Json =
    
    open Newtonsoft.Json
    
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

    type CustomFeedIdConverter () =
        inherit JsonConverter<CustomFeedId> ()
        override _.WriteJson (writer : JsonWriter, value : CustomFeedId, _ : JsonSerializer) =
            writer.WriteValue (CustomFeedId.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : CustomFeedId, _ : bool, _ : JsonSerializer) =
            (string >> CustomFeedId) reader.Value

    type CustomFeedSourceConverter () =
        inherit JsonConverter<CustomFeedSource> ()
        override _.WriteJson (writer : JsonWriter, value : CustomFeedSource, _ : JsonSerializer) =
            writer.WriteValue (CustomFeedSource.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : CustomFeedSource, _ : bool, _ : JsonSerializer) =
            (string >> CustomFeedSource.parse) reader.Value
            
    type ExplicitRatingConverter () =
        inherit JsonConverter<ExplicitRating> ()
        override _.WriteJson (writer : JsonWriter, value : ExplicitRating, _ : JsonSerializer) =
            writer.WriteValue (ExplicitRating.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : ExplicitRating, _ : bool, _ : JsonSerializer) =
            (string >> ExplicitRating.parse) reader.Value
        
    type MarkupTextConverter () =
        inherit JsonConverter<MarkupText> ()
        override _.WriteJson (writer : JsonWriter, value : MarkupText, _ : JsonSerializer) =
            writer.WriteValue (MarkupText.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : MarkupText, _ : bool, _ : JsonSerializer) =
            (string >> MarkupText.parse) reader.Value
            
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

    type TagMapIdConverter () =
        inherit JsonConverter<TagMapId> ()
        override _.WriteJson (writer : JsonWriter, value : TagMapId, _ : JsonSerializer) =
            writer.WriteValue (TagMapId.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : TagMapId, _ : bool, _ : JsonSerializer) =
            (string >> TagMapId) reader.Value

    type ThemeAssetIdConverter () =
        inherit JsonConverter<ThemeAssetId> ()
        override _.WriteJson (writer : JsonWriter, value : ThemeAssetId, _ : JsonSerializer) =
            writer.WriteValue (ThemeAssetId.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : ThemeAssetId, _ : bool, _ : JsonSerializer) =
            (string >> ThemeAssetId.ofString) reader.Value

    type ThemeIdConverter () =
        inherit JsonConverter<ThemeId> ()
        override _.WriteJson (writer : JsonWriter, value : ThemeId, _ : JsonSerializer) =
            writer.WriteValue (ThemeId.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : ThemeId, _ : bool, _ : JsonSerializer) =
            (string >> ThemeId) reader.Value
        
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
            CategoryIdConverter       ()
            CommentIdConverter        ()
            CustomFeedIdConverter     ()
            CustomFeedSourceConverter ()
            ExplicitRatingConverter   ()
            MarkupTextConverter       ()
            PermalinkConverter        ()
            PageIdConverter           ()
            PostIdConverter           ()
            TagMapIdConverter         ()
            ThemeAssetIdConverter     ()
            ThemeIdConverter          ()
            WebLogIdConverter         ()
            WebLogUserIdConverter     ()
            // Handles DUs with no associated data, as well as option fields
            CompactUnionJsonConverter ()
        }


// We *like* the implicit conversion of string to BsonValue
#nowarn "3391"

/// BSON converters for use with LiteDB
module Bson =
    
    open LiteDB
    
    module AuthorizationLevelMapping =
        let fromBson (value : BsonValue) = AuthorizationLevel.parse value.AsString
        let toBson (value : AuthorizationLevel) : BsonValue = AuthorizationLevel.toString value
    
    module CategoryIdMapping =
        let fromBson (value : BsonValue) = CategoryId value.AsString
        let toBson (value : CategoryId) : BsonValue = CategoryId.toString value
    
    module CommentIdMapping =
        let fromBson (value : BsonValue) = CommentId value.AsString
        let toBson (value : CommentId) : BsonValue = CommentId.toString value
    
    module CommentStatusMapping =
        let fromBson (value : BsonValue) = CommentStatus.parse value.AsString
        let toBson (value : CommentStatus) : BsonValue = CommentStatus.toString value
    
    module CustomFeedIdMapping =
        let fromBson (value : BsonValue) = CustomFeedId value.AsString
        let toBson (value : CustomFeedId) : BsonValue = CustomFeedId.toString value
    
    module CustomFeedSourceMapping =
        let fromBson (value : BsonValue) = CustomFeedSource.parse value.AsString
        let toBson (value : CustomFeedSource) : BsonValue = CustomFeedSource.toString value
    
    module ExplicitRatingMapping =
        let fromBson (value : BsonValue) = ExplicitRating.parse value.AsString
        let toBson (value : ExplicitRating) : BsonValue = ExplicitRating.toString value
        
    module MarkupTextMapping =
        let fromBson (value : BsonValue) = MarkupText.parse value.AsString
        let toBson (value : MarkupText) : BsonValue = MarkupText.toString value
    
    module OptionMapping =
        let categoryIdFromBson (value : BsonValue) = if value.IsNull then None else Some (CategoryId value.AsString)
        let categoryIdToBson (value : CategoryId option) : BsonValue =
            match value with Some (CategoryId catId) -> catId | None -> BsonValue.Null
        
        let commentIdFromBson (value : BsonValue) = if value.IsNull then None else Some (CommentId value.AsString)
        let commentIdToBson (value : CommentId option) : BsonValue =
            match value with Some (CommentId comId) -> comId | None -> BsonValue.Null
        
        let dateTimeFromBson (value : BsonValue) = if value.IsNull then None else Some value.AsDateTime
        let dateTimeToBson (value : DateTime option) : BsonValue =
            match value with Some dateTime -> dateTime | None -> BsonValue.Null
        
        let intFromBson (value : BsonValue) = if value.IsNull then None else Some value.AsInt32
        let intToBson (value : int option) : BsonValue = match value with Some nbr -> nbr | None -> BsonValue.Null
        
        let podcastOptionsFromBson (value : BsonValue) =
            if value.IsNull then None else Some (BsonMapper.Global.ToObject<PodcastOptions> value.AsDocument)
        let podcastOptionsToBson (value : PodcastOptions option) : BsonValue =
            match value with Some opts -> BsonMapper.Global.ToDocument opts | None -> BsonValue.Null
        
        let stringFromBson (value : BsonValue) = if value.IsNull then None else Some value.AsString
        let stringToBson (value : string option) : BsonValue = match value with Some str -> str | None -> BsonValue.Null
            
    module PermalinkMapping =
        let fromBson (value : BsonValue) = Permalink value.AsString
        let toBson (value : Permalink) : BsonValue = Permalink.toString value
    
    module PageIdMapping =
        let fromBson (value : BsonValue) = PageId value.AsString
        let toBson (value : PageId) : BsonValue = PageId.toString value
    
    module PostIdMapping =
        let fromBson (value : BsonValue) = PostId value.AsString
        let toBson (value : PostId) : BsonValue = PostId.toString value
    
    module PostStatusMapping =
        let fromBson (value : BsonValue) = PostStatus.parse value.AsString
        let toBson (value : PostStatus) : BsonValue = PostStatus.toString value
    
    module TagMapIdMapping =
        let fromBson (value : BsonValue) = TagMapId value.AsString
        let toBson (value : TagMapId) : BsonValue = TagMapId.toString value
    
    module ThemeAssetIdMapping =
        let fromBson (value : BsonValue) = ThemeAssetId.ofString value.AsString
        let toBson (value : ThemeAssetId) : BsonValue = ThemeAssetId.toString value
    
    module ThemeIdMapping =
        let fromBson (value : BsonValue) = ThemeId value.AsString
        let toBson (value : ThemeId) : BsonValue = ThemeId.toString value
    
    module WebLogIdMapping =
        let fromBson (value : BsonValue) = WebLogId value.AsString
        let toBson (value : WebLogId) : BsonValue = WebLogId.toString value
    
    module WebLogUserIdMapping =
        let fromBson (value : BsonValue) = WebLogUserId value.AsString
        let toBson (value : WebLogUserId) : BsonValue = WebLogUserId.toString value
    
    /// Register all BSON mappings
    let registerAll () =
        let g = BsonMapper.Global
        g.RegisterType<AuthorizationLevel> (AuthorizationLevelMapping.toBson, AuthorizationLevelMapping.fromBson)
        g.RegisterType<CategoryId>         (CategoryIdMapping.toBson,         CategoryIdMapping.fromBson)
        g.RegisterType<CommentId>          (CommentIdMapping.toBson,          CommentIdMapping.fromBson)
        g.RegisterType<CommentStatus>      (CommentStatusMapping.toBson,      CommentStatusMapping.fromBson)
        g.RegisterType<CustomFeedId>       (CustomFeedIdMapping.toBson,       CustomFeedIdMapping.fromBson)
        g.RegisterType<CustomFeedSource>   (CustomFeedSourceMapping.toBson,   CustomFeedSourceMapping.fromBson)
        g.RegisterType<ExplicitRating>     (ExplicitRatingMapping.toBson,     ExplicitRatingMapping.fromBson)
        g.RegisterType<MarkupText>         (MarkupTextMapping.toBson,         MarkupTextMapping.fromBson)
        g.RegisterType<Permalink>          (PermalinkMapping.toBson,          PermalinkMapping.fromBson)
        g.RegisterType<PageId>             (PageIdMapping.toBson,             PageIdMapping.fromBson)
        g.RegisterType<PostId>             (PostIdMapping.toBson,             PostIdMapping.fromBson)
        g.RegisterType<PostStatus>         (PostStatusMapping.toBson,         PostStatusMapping.fromBson)
        g.RegisterType<TagMapId>           (TagMapIdMapping.toBson,           TagMapIdMapping.fromBson)
        g.RegisterType<ThemeAssetId>       (ThemeAssetIdMapping.toBson,       ThemeAssetIdMapping.fromBson)
        g.RegisterType<ThemeId>            (ThemeIdMapping.toBson,            ThemeIdMapping.fromBson)
        g.RegisterType<WebLogId>           (WebLogIdMapping.toBson,           WebLogIdMapping.fromBson)
        g.RegisterType<WebLogUserId>       (WebLogUserIdMapping.toBson,       WebLogUserIdMapping.fromBson)
        
        g.RegisterType<CategoryId     option> (OptionMapping.categoryIdToBson,     OptionMapping.categoryIdFromBson)
        g.RegisterType<CommentId      option> (OptionMapping.commentIdToBson,      OptionMapping.commentIdFromBson)
        g.RegisterType<DateTime       option> (OptionMapping.dateTimeToBson,       OptionMapping.dateTimeFromBson)
        g.RegisterType<int            option> (OptionMapping.intToBson,            OptionMapping.intFromBson)
        g.RegisterType<PodcastOptions option> (OptionMapping.podcastOptionsToBson, OptionMapping.podcastOptionsFromBson)
        g.RegisterType<string         option> (OptionMapping.stringToBson,         OptionMapping.stringFromBson)
        