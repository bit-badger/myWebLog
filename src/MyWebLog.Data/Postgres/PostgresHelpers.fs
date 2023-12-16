/// Helper functions for the PostgreSQL data implementation
[<AutoOpen>]
module MyWebLog.Data.Postgres.PostgresHelpers

/// The table names used in the PostgreSQL implementation
[<RequireQualifiedAccess>]
module Table =
    
    /// Categories
    [<Literal>]
    let Category = "category"
    
    /// Database Version
    [<Literal>]
    let DbVersion = "db_version"
    
    /// Pages
    [<Literal>]
    let Page = "page"
    
    /// Page Revisions
    [<Literal>]
    let PageRevision = "page_revision"
    
    /// Posts
    [<Literal>]
    let Post = "post"
    
    /// Post Comments
    [<Literal>]
    let PostComment = "post_comment"
    
    /// Post Revisions
    [<Literal>]
    let PostRevision = "post_revision"
    
    /// Tag/URL Mappings
    [<Literal>]
    let TagMap = "tag_map"
    
    /// Themes
    [<Literal>]
    let Theme = "theme"
    
    /// Theme Assets
    [<Literal>]
    let ThemeAsset = "theme_asset"
    
    /// Uploads
    [<Literal>]
    let Upload = "upload"
    
    /// Web Logs
    [<Literal>]
    let WebLog = "web_log"
    
    /// Users
    [<Literal>]
    let WebLogUser = "web_log_user"


open System
open System.Threading.Tasks
open BitBadger.Npgsql.FSharp.Documents
open MyWebLog
open MyWebLog.Data
open NodaTime
open Npgsql
open Npgsql.FSharp

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (string webLogId)

/// Create an anonymous record with the given web log ID
let webLogDoc (webLogId : WebLogId) =
    {| WebLogId = webLogId |}

/// Create a parameter for a web log document-contains query
let webLogContains webLogId =
    "@criteria", Query.jsonbDocParam (webLogDoc webLogId)

/// The name of the field to select to be able to use Map.toCount
let countName = "the_count"

/// The name of the field to select to be able to use Map.toExists
let existsName = "does_exist"

/// A SQL string to select data from a table with the given JSON document contains criteria
let selectWithCriteria tableName =
    $"""{Query.selectFromTable tableName} WHERE {Query.whereDataContains "@criteria"}"""

/// Create the SQL and parameters for an IN clause
let inClause<'T> colNameAndPrefix paramName (valueFunc: 'T -> string) (items : 'T list) =
    if List.isEmpty items then "", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS}, @%s{paramName}{idx}", ($"@%s{paramName}{idx}", Sql.string (valueFunc it)) :: itemP)
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"%s{colNameAndPrefix} IN (@%s{paramName}0", [ $"@%s{paramName}0", Sql.string (valueFunc it) ])
             |> Seq.head)
        |> function sql, ps -> $"{sql})", ps

/// Create the SQL and parameters for match-any array query
let arrayContains<'T> name (valueFunc : 'T -> string) (items : 'T list) =
    $"data['{name}'] ?| @{name}Values",
    ($"@{name}Values", Sql.stringArray (items |> List.map valueFunc |> Array.ofList))

/// Get the first result of the given query
let tryHead<'T> (query : Task<'T list>) = backgroundTask {
    let! results = query
    return List.tryHead results
}

/// Create a parameter for a non-standard type
let typedParam<'T> name (it : 'T) =
    $"@%s{name}", Sql.parameter (NpgsqlParameter ($"@{name}", it))

/// Create a parameter for a possibly-missing non-standard type
let optParam<'T> name (it : 'T option) =
    let p = NpgsqlParameter ($"@%s{name}", if Option.isSome it then box it.Value else DBNull.Value)
    p.ParameterName, Sql.parameter p

/// Mapping functions for SQL queries
module Map =
    
    /// Get a count from a row
    let toCount (row : RowReader) =
        row.int countName
    
    /// Get a true/false value as to whether an item exists
    let toExists (row : RowReader) =
        row.bool existsName
    
    /// Create a permalink from the current row
    let toPermalink (row : RowReader) =
        Permalink (row.string "permalink")
    
    /// Create a revision from the current row
    let toRevision (row : RowReader) : Revision =
        {   AsOf = row.fieldValue<Instant> "as_of"
            Text = row.string              "revision_text" |> MarkupText.Parse
        }
    
    /// Create a theme asset from the current row
    let toThemeAsset includeData (row : RowReader) : ThemeAsset =
        {   Id        = ThemeAssetId (ThemeId (row.string "theme_id"), row.string "path")
            UpdatedOn = row.fieldValue<Instant> "updated_on"
            Data      = if includeData then row.bytea "data" else [||]
        }
    
    /// Create an uploaded file from the current row
    let toUpload includeData (row : RowReader) : Upload =
        {   Id        = row.string              "id"         |> UploadId
            WebLogId  = row.string              "web_log_id" |> WebLogId
            Path      = row.string              "path"       |> Permalink
            UpdatedOn = row.fieldValue<Instant> "updated_on"
            Data      = if includeData then row.bytea "data" else [||]
        }

/// Document manipulation functions
module Document =
    
    /// Determine whether a document exists with the given key for the given web log
    let existsByWebLog<'TKey> table (key : 'TKey) (keyFunc : 'TKey -> string) webLogId =
        Custom.scalar
            $""" SELECT EXISTS (
                          SELECT 1 FROM %s{table} WHERE id = @id AND {Query.whereDataContains "@criteria"}
                        ) AS {existsName}"""
            [ "@id", Sql.string (keyFunc key); webLogContains webLogId ] Map.toExists
    
    /// Find a document by its ID for the given web log
    let findByIdAndWebLog<'TKey, 'TDoc> table (key : 'TKey) (keyFunc : 'TKey -> string) webLogId =
        Custom.single $"""{Query.selectFromTable table} WHERE id = @id AND {Query.whereDataContains "@criteria"}"""
                      [ "@id", Sql.string (keyFunc key); webLogContains webLogId ] fromData<'TDoc>
    
    /// Find a document by its ID for the given web log
    let findByWebLog<'TDoc> table webLogId : Task<'TDoc list> =
        Find.byContains table (webLogDoc webLogId)
    

/// Functions to support revisions
module Revisions =
    
    /// Find all revisions for the given entity
    let findByEntityId<'TKey> revTable entityTable (key : 'TKey) (keyFunc : 'TKey -> string) =
        Custom.list $"SELECT as_of, revision_text FROM %s{revTable} WHERE %s{entityTable}_id = @id ORDER BY as_of DESC"
                    [ "@id", Sql.string (keyFunc key) ] Map.toRevision
    
    /// Find all revisions for all posts for the given web log
    let findByWebLog<'TKey> revTable entityTable (keyFunc : string -> 'TKey) webLogId =
        Custom.list
            $"""SELECT pr.*
                  FROM %s{revTable} pr
                       INNER JOIN %s{entityTable} p ON p.id = pr.{entityTable}_id
                 WHERE p.{Query.whereDataContains "@criteria"}
                 ORDER BY as_of DESC"""
            [ webLogContains webLogId ] (fun row -> keyFunc (row.string $"{entityTable}_id"), Map.toRevision row)

    /// Parameters for a revision INSERT statement
    let revParams<'TKey> (key : 'TKey) (keyFunc : 'TKey -> string) rev = [
        typedParam "asOf" rev.AsOf
        "@id",   Sql.string (keyFunc key)
        "@text", Sql.string (string rev.Text)
    ]
    
    /// The SQL statement to insert a revision
    let insertSql table =
        $"INSERT INTO %s{table} VALUES (@id, @asOf, @text)"
    
    /// Update a page's revisions
    let update<'TKey> revTable entityTable (key : 'TKey) (keyFunc : 'TKey -> string) oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            let! _ =
                Configuration.dataSource ()
                |> Sql.fromDataSource
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        $"DELETE FROM %s{revTable} WHERE %s{entityTable}_id = @id AND as_of = @asOf",
                        toDelete
                        |> List.map (fun it -> [
                            "@id", Sql.string (keyFunc key)
                            typedParam "asOf" it.AsOf
                        ])
                    if not (List.isEmpty toAdd) then
                        insertSql revTable, toAdd |> List.map (revParams key keyFunc)
                ]
            ()
    }

