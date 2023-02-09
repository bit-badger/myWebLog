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

    type PodcastMediumConverter () =
        inherit JsonConverter<PodcastMedium> ()
        override _.WriteJson (writer : JsonWriter, value : PodcastMedium, _ : JsonSerializer) =
            writer.WriteValue (PodcastMedium.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : PodcastMedium, _ : bool, _ : JsonSerializer) =
            (string >> PodcastMedium.parse) reader.Value

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
    
    type UploadIdConverter () =
        inherit JsonConverter<UploadId> ()
        override _.WriteJson (writer : JsonWriter, value : UploadId, _ : JsonSerializer) =
            writer.WriteValue (UploadId.toString value)
        override _.ReadJson (reader : JsonReader, _ : Type, _ : UploadId, _ : bool, _ : JsonSerializer) =
            (string >> UploadId) reader.Value
    
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
    open NodaTime
    open NodaTime.Serialization.JsonNet
    
    /// Configure a serializer to use these converters
    let configure (ser : JsonSerializer) =
        // Our converters
        [   CategoryIdConverter       () :> JsonConverter
            CommentIdConverter        ()
            CustomFeedIdConverter     ()
            CustomFeedSourceConverter ()
            ExplicitRatingConverter   ()
            MarkupTextConverter       ()
            PermalinkConverter        ()
            PageIdConverter           ()
            PodcastMediumConverter    ()
            PostIdConverter           ()
            TagMapIdConverter         ()
            ThemeAssetIdConverter     ()
            ThemeIdConverter          ()
            UploadIdConverter         ()
            WebLogIdConverter         ()
            WebLogUserIdConverter     ()
        ] |> List.iter ser.Converters.Add
        // NodaTime
        let _ = ser.ConfigureForNodaTime DateTimeZoneProviders.Tzdb
        // Handles DUs with no associated data, as well as option fields
        ser.Converters.Add (CompactUnionJsonConverter ())
        ser.NullValueHandling     <- NullValueHandling.Ignore
        ser.MissingMemberHandling <- MissingMemberHandling.Ignore
        ser
    
    /// Serializer settings extracted from a JsonSerializer (a property sure would be nice...)
    let mutable private serializerSettings : JsonSerializerSettings option = None
    
    /// Extract settings from the serializer to be used in JsonConvert calls
    let settings (ser : JsonSerializer) =
        if Option.isNone serializerSettings then
            serializerSettings <- JsonSerializerSettings (
                ConstructorHandling            = ser.ConstructorHandling,
                ContractResolver               = ser.ContractResolver,
                Converters                     = ser.Converters,
                DefaultValueHandling           = ser.DefaultValueHandling,
                DateFormatHandling             = ser.DateFormatHandling,
                DateParseHandling              = ser.DateParseHandling,
                MetadataPropertyHandling       = ser.MetadataPropertyHandling,
                MissingMemberHandling          = ser.MissingMemberHandling,
                NullValueHandling              = ser.NullValueHandling,
                ObjectCreationHandling         = ser.ObjectCreationHandling,
                ReferenceLoopHandling          = ser.ReferenceLoopHandling,
                SerializationBinder            = ser.SerializationBinder,
                TraceWriter                    = ser.TraceWriter,
                TypeNameAssemblyFormatHandling = ser.TypeNameAssemblyFormatHandling,
                TypeNameHandling               = ser.TypeNameHandling)
                |> Some
        serializerSettings.Value
