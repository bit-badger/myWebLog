/// Helper functions for the PostgreSQL data implementation
[<AutoOpen>]
module MyWebLog.Data.PostgreSql.PostgreSqlHelpers

open MyWebLog
open Newtonsoft.Json
open Npgsql.FSharp

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (WebLogId.toString webLogId)

/// Create the SQL and parameters to find a page or post by one or more prior permalinks
let priorPermalinkSql permalinks =
    let mutable idx = 0
    permalinks
    |> List.skip 1
    |> List.fold (fun (linkSql, linkParams) it ->
        idx <- idx + 1
        $"{linkSql} OR prior_permalinks && ARRAY[@link{idx}]",
        ($"@link{idx}", Sql.string (Permalink.toString it)) :: linkParams)
        (Seq.ofList permalinks
         |> Seq.map (fun it ->
             "prior_permalinks && ARRAY[@link0]", [ "@link0", Sql.string (Permalink.toString it) ])
         |> Seq.head)

/// Create the SQL and parameters for an IN clause
let inClause<'T> name (valueFunc: 'T -> string) (items : 'T list) =
    let mutable idx = 0
    items
    |> List.skip 1
    |> List.fold (fun (itemS, itemP) it ->
        idx <- idx + 1
        $"{itemS}, @%s{name}{idx}", ($"@%s{name}{idx}", Sql.string (valueFunc it)) :: itemP)
        (Seq.ofList items
         |> Seq.map (fun it -> $"@%s{name}0", [ $"@%s{name}0", Sql.string (valueFunc it) ])
         |> Seq.head)

/// Mapping functions for SQL queries
module Map =
    
    /// Map an id field to a category ID
    let toCategoryId (row : RowReader) =
        CategoryId (row.string "id")
    
    /// Create a category from the current row
    let toCategory (row : RowReader) : Category =
        {   Id          = toCategoryId row
            WebLogId    = row.string       "web_log_id" |> WebLogId
            Name        = row.string       "name"
            Slug        = row.string       "slug"
            Description = row.stringOrNone "description"
            ParentId    = row.stringOrNone "parent_id"  |> Option.map CategoryId
        }

    /// Get a count from a row
    let toCount (row : RowReader) =
        row.int "the_count"
    
    /// Create a meta item from the current row
    let toMetaItem (row : RowReader) : MetaItem =
        {   Name  = row.string "name"
            Value = row.string "value"
        }
    
    /// Create a permalink from the current row
    let toPermalink (row : RowReader) =
        Permalink (row.string "permalink")
    
    /// Create a page from the current row
    let toPage (row : RowReader) : Page =
        { Page.empty with
            Id              = row.string       "id"         |> PageId
            WebLogId        = row.string       "web_log_id" |> WebLogId
            AuthorId        = row.string       "author_id"  |> WebLogUserId
            Title           = row.string       "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray  "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.dateTime     "published_on"
            UpdatedOn       = row.dateTime     "updated_on"
            IsInPageList    = row.bool         "is_in_page_list"
            Template        = row.stringOrNone "template"
            Text            = row.string       "page_text"
            Metadata        = row.stringOrNone "meta_items"
                              |> Option.map JsonConvert.DeserializeObject<MetaItem list>
                              |> Option.defaultValue []
        }
    
    /// Create a post from the current row
    let toPost (row : RowReader) : Post =
        { Post.empty with
            Id              = row.string            "id"         |> PostId
            WebLogId        = row.string            "web_log_id" |> WebLogId
            AuthorId        = row.string            "author_id"  |> WebLogUserId
            Status          = row.string            "status"     |> PostStatus.parse
            Title           = row.string            "title"
            Permalink       = toPermalink row
            PriorPermalinks = row.stringArray       "prior_permalinks" |> Array.map Permalink |> List.ofArray
            PublishedOn     = row.dateTimeOrNone    "published_on"
            UpdatedOn       = row.dateTime          "updated_on"
            Template        = row.stringOrNone      "template"
            Text            = row.string            "post_text"
            CategoryIds     = row.stringArrayOrNone "category_ids"
                              |> Option.map (Array.map CategoryId >> List.ofArray)
                              |> Option.defaultValue []
            Tags            = row.stringArrayOrNone "tags"
                              |> Option.map List.ofArray
                              |> Option.defaultValue []
            Metadata        = row.stringOrNone      "meta_items"
                              |> Option.map JsonConvert.DeserializeObject<MetaItem list>
                              |> Option.defaultValue []
            Episode         = row.stringOrNone      "episode" |> Option.map JsonConvert.DeserializeObject<Episode>
        }
    
    /// Create a revision from the current row
    let toRevision (row : RowReader) : Revision =
        {   AsOf = row.dateTime "as_of"
            Text = row.string   "revision_text" |> MarkupText.parse
        }
    
    /// Create a tag mapping from the current row in the given data reader
    let toTagMap (row : RowReader) : TagMap =
        {   Id       = row.string "id"         |> TagMapId
            WebLogId = row.string "web_log_id" |> WebLogId
            Tag      = row.string "tag"
            UrlValue = row.string "url_value"
        }
