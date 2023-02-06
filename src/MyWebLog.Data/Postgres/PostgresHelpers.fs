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
open MyWebLog
open MyWebLog.Data
open NodaTime
open Npgsql
open Npgsql.FSharp

/// Create a WHERE clause fragment for the web log ID
let webLogWhere = "data ->> 'WebLogId' = @webLogId"

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (WebLogId.toString webLogId)

/// The name of the field to select to be able to use Map.toCount
let countName = "the_count"

/// The name of the field to select to be able to use Map.toExists
let existsName = "does_exist"

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

/// Create the SQL and parameters for the array-in-JSON equivalent of an IN clause
let jsonArrayInClause<'T> name (valueFunc : 'T -> string) (items : 'T list) =
    if List.isEmpty items then "TRUE = FALSE", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS} OR data -> '%s{name}' ? @{name}{idx}",
            ($"@{name}{idx}", Sql.jsonb (valueFunc it)) :: itemP)
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"data -> '{name}' ? @{name}0", [ $"@{name}0", Sql.string (valueFunc it) ])
             |> Seq.head)
    
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

/// SQL statement to insert into a document table
let docInsertSql table =
    $"INSERT INTO %s{table} VALUES (@id, @data)"

/// SQL statement to select a document by its ID
let docSelectSql table =
    $"SELECT * FROM %s{table} WHERE id = @id"

/// SQL statement to select documents by their web log IDs
let docSelectForWebLogSql table =
    $"SELECT * FROM %s{table} WHERE {webLogWhere}"

/// SQL statement to update a document in a document table
let docUpdateSql table =
    $"UPDATE %s{table} SET data = @data WHERE id = @id"

/// SQL statement to insert or update a document in a document table
let docUpsertSql table =
    $"{docInsertSql table} ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data"

/// SQL statement to delete a document from a document table by its ID
let docDeleteSql table =
    $"DELETE FROM %s{table} WHERE id = @id"

/// SQL statement to count documents for a web log
let docCountForWebLogSql table =
    $"SELECT COUNT(id) AS {countName} FROM %s{table} WHERE {webLogWhere}"

/// SQL statement to determine if a document exists for a web log 
let docExistsForWebLogSql table =
    $"SELECT EXISTS (SELECT 1 FROM %s{table} WHERE id = @id AND {webLogWhere}) AS {existsName}"

/// Mapping functions for SQL queries
module Map =
    
    /// Map an item by deserializing the document
    let fromDoc<'T> ser (row : RowReader) =
        Utils.deserialize<'T> ser (row.string "data")
    
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
            Text = row.string              "revision_text" |> MarkupText.parse
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
    
    /// Convert extra SQL to a for that can be appended to a query  
    let private moreSql sql = sql |> Option.map (fun it -> $" %s{it}") |> Option.defaultValue ""
    
    /// Create a parameter for a @> (contains) query
    let contains<'T> (name : string) ser (value : 'T) =
        name, Sql.jsonb (Utils.serialize ser value)

    /// Count documents for a web log
    let countByWebLog conn table webLogId extraSql =
        Sql.existingConnection conn
        |> Sql.query $"{docCountForWebLogSql table}{moreSql extraSql}"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toCount

    /// Delete a document
    let delete conn table idParam = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query (docDeleteSql table)
            |> Sql.parameters [ "@id", Sql.string idParam ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Determine if a document with the given ID exists
    let exists<'TKey> conn table (key : 'TKey) (keyFunc : 'TKey -> string) =
        Sql.existingConnection conn
        |> Sql.query $"SELECT EXISTS (SELECT 1 FROM %s{table} WHERE id = @id) AS {existsName}"
        |> Sql.parameters [ "@id", Sql.string (keyFunc key) ]
        |> Sql.executeRowAsync Map.toExists

    /// Determine whether a document exists with the given key for the given web log
    let existsByWebLog<'TKey> conn table (key : 'TKey) (keyFunc : 'TKey -> string) webLogId =  
        Sql.existingConnection conn
        |> Sql.query (docExistsForWebLogSql table)
        |> Sql.parameters [ "@id", Sql.string (keyFunc key); webLogIdParam webLogId ]
        |> Sql.executeRowAsync Map.toExists
    
    /// Find a document by its ID
    let findById<'TKey, 'TDoc> conn table (key : 'TKey) (keyFunc : 'TKey -> string) (docFunc : RowReader -> 'TDoc) =
        Sql.existingConnection conn
        |> Sql.query (docSelectSql table)
        |> Sql.parameters [ "@id", Sql.string (keyFunc key) ]
        |> Sql.executeAsync docFunc
        |> tryHead
    
    /// Find a document by its ID for the given web log
    let findByIdAndWebLog<'TKey, 'TDoc> conn table (key : 'TKey) (keyFunc : 'TKey -> string) webLogId
                                        (docFunc : RowReader -> 'TDoc) =
        Sql.existingConnection conn
        |> Sql.query $"{docSelectSql table} AND {webLogWhere}"
        |> Sql.parameters [ "@id", Sql.string (keyFunc key); webLogIdParam webLogId ]
        |> Sql.executeAsync docFunc
        |> tryHead
    
    /// Find all documents for the given web log
    let findByWebLog<'TDoc> conn table webLogId (docFunc : RowReader -> 'TDoc) extraSql =
        Sql.existingConnection conn
        |> Sql.query $"{docSelectForWebLogSql table}{moreSql extraSql}"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync docFunc

    /// Insert a new document
    let insert<'T> conn table (paramFunc : 'T -> (string * SqlValue) list) (doc : 'T) = task {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query (docInsertSql table)
            |> Sql.parameters (paramFunc doc)
            |> Sql.executeNonQueryAsync
        ()
    }
        
    /// Update an existing document
    let update<'T> conn table (paramFunc : 'T -> (string * SqlValue) list) (doc : 'T) = task {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query (docUpdateSql table)
            |> Sql.parameters (paramFunc doc)
            |> Sql.executeNonQueryAsync
        ()
    }
        
    /// Insert or update a document
    let upsert<'T> conn table (paramFunc : 'T -> (string * SqlValue) list) (doc : 'T) = task {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query (docUpsertSql table)
            |> Sql.parameters (paramFunc doc)
            |> Sql.executeNonQueryAsync
        ()
    }


/// Functions to support revisions
module Revisions =
    
    /// Find all revisions for the given entity
    let findByEntityId<'TKey> conn revTable entityTable (key : 'TKey) (keyFunc : 'TKey -> string) =
        Sql.existingConnection conn
        |> Sql.query $"SELECT as_of, revision_text FROM %s{revTable} WHERE %s{entityTable}_id = @id ORDER BY as_of DESC"
        |> Sql.parameters [ "@id", Sql.string (keyFunc key) ]
        |> Sql.executeAsync Map.toRevision
    
    /// Find all revisions for all posts for the given web log
    let findByWebLog<'TKey> conn revTable entityTable (keyFunc : string -> 'TKey) webLogId =
        Sql.existingConnection conn
        |> Sql.query $"
            SELECT pr.*
              FROM %s{revTable} pr
                   INNER JOIN %s{entityTable} p ON p.id = pr.{entityTable}_id
             WHERE p.{webLogWhere}
             ORDER BY as_of DESC"
        |> Sql.parameters [ webLogIdParam webLogId ]
        |> Sql.executeAsync (fun row -> keyFunc (row.string $"{entityTable}_id"), Map.toRevision row)

    /// Parameters for a revision INSERT statement
    let revParams<'TKey> (key : 'TKey) (keyFunc : 'TKey -> string) rev = [
        typedParam "asOf" rev.AsOf
        "@id",   Sql.string (keyFunc key)
        "@text", Sql.string (MarkupText.toString rev.Text)
    ]
    
    /// The SQL statement to insert a revision
    let insertSql table =
        $"INSERT INTO %s{table} VALUES (@id, @asOf, @text)"
    
    /// Update a page's revisions
    let update<'TKey>
            conn revTable entityTable (key : 'TKey) (keyFunc : 'TKey -> string) oldRevs newRevs = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            let! _ =
                Sql.existingConnection conn
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

