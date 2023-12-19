/// Helper functions for the SQLite data implementation
[<AutoOpen>]
module MyWebLog.Data.SQLite.Helpers

/// The table names used in the SQLite implementation
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
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data
open NodaTime.Text

/// Run a command that returns a count
let count (cmd: SqliteCommand) = backgroundTask {
    let! it = cmd.ExecuteScalarAsync()
    return int (it :?> int64)
}

/// Create a list of items from the given data reader
let toList<'T> (it: SqliteDataReader -> 'T) (rdr: SqliteDataReader) =
    seq { while rdr.Read () do it rdr }
    |> List.ofSeq

/// Execute a command that returns no data
let write (cmd: SqliteCommand) = backgroundTask {
    let! _ = cmd.ExecuteNonQueryAsync()
    ()
}

/// Add a possibly-missing parameter, substituting null for None
let maybe<'T> (it: 'T option) : obj = match it with Some x -> x :> obj | None -> DBNull.Value

/// Create a value for a Duration
let durationParam =
    DurationPattern.Roundtrip.Format

/// Create a value for an Instant
let instantParam =
    InstantPattern.General.Format

/// Create an optional value for a Duration
let maybeDuration =
    Option.map durationParam >> maybe

/// Create an optional value for an Instant
let maybeInstant =
    Option.map instantParam >> maybe

/// Create the SQL and parameters for an EXISTS applied to a JSON array
let inJsonArray<'T> table jsonField paramName (items: 'T list) =
    if List.isEmpty items then "", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS}, @%s{paramName}{idx}", (SqliteParameter($"@%s{paramName}{idx}", string it) :: itemP))
            (Seq.ofList items
             |> Seq.map (fun it -> $"(@%s{paramName}0", [ SqliteParameter($"@%s{paramName}0", string it) ])
             |> Seq.head)
        |> function
            sql, ps ->
                $"EXISTS (SELECT 1 FROM json_each(%s{table}.data, '$.%s{jsonField}') WHERE value IN {sql}))", ps

/// Create the SQL and parameters for an IN clause
let inClause<'T> colNameAndPrefix paramName (valueFunc: 'T -> string) (items: 'T list) =
    if List.isEmpty items then "", []
    else
        let mutable idx = 0
        items
        |> List.skip 1
        |> List.fold (fun (itemS, itemP) it ->
            idx <- idx + 1
            $"{itemS}, @%s{paramName}{idx}", (SqliteParameter ($"@%s{paramName}{idx}", valueFunc it) :: itemP))
            (Seq.ofList items
             |> Seq.map (fun it ->
                 $"%s{colNameAndPrefix} IN (@%s{paramName}0", [ SqliteParameter ($"@%s{paramName}0", valueFunc it) ])
             |> Seq.head)
        |> function sql, ps -> $"{sql})", ps


/// Functions to map domain items from a data reader
module Map =
    
    open System.IO
    
    /// Get a boolean value from a data reader
    let getBoolean col (rdr: SqliteDataReader) = rdr.GetBoolean(rdr.GetOrdinal col)
    
    /// Get a date/time value from a data reader
    let getDateTime col (rdr: SqliteDataReader) = rdr.GetDateTime(rdr.GetOrdinal col)
    
    /// Get a Guid value from a data reader
    let getGuid col (rdr: SqliteDataReader) = rdr.GetGuid(rdr.GetOrdinal col)
    
    /// Get an int value from a data reader
    let getInt col (rdr: SqliteDataReader) = rdr.GetInt32(rdr.GetOrdinal col)
    
    /// Get a long (64-bit int) value from a data reader
    let getLong col (rdr: SqliteDataReader) = rdr.GetInt64(rdr.GetOrdinal col)
    
    /// Get a BLOB stream value from a data reader
    let getStream col (rdr: SqliteDataReader) = rdr.GetStream(rdr.GetOrdinal col)
    
    /// Get a string value from a data reader
    let getString col (rdr: SqliteDataReader) = rdr.GetString(rdr.GetOrdinal col)
    
    /// Parse an Instant from the given value
    let parseInstant value =
        match InstantPattern.General.Parse value with
        | it when it.Success -> it.Value
        | it -> raise it.Exception
    
    /// Get an Instant value from a data reader
    let getInstant col rdr =
        getString col rdr |> parseInstant
    
    /// Get a timespan value from a data reader
    let getTimeSpan col (rdr: SqliteDataReader) = rdr.GetTimeSpan(rdr.GetOrdinal col)
    
    /// Get a possibly null boolean value from a data reader
    let tryBoolean col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getBoolean col rdr)
    
    /// Get a possibly null date/time value from a data reader
    let tryDateTime col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getDateTime col rdr)
    
    /// Get a possibly null Guid value from a data reader
    let tryGuid col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getGuid col rdr)
    
    /// Get a possibly null int value from a data reader
    let tryInt col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getInt col rdr)
    
    /// Get a possibly null string value from a data reader
    let tryString col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getString col rdr)
    
    /// Get a possibly null timespan value from a data reader
    let tryTimeSpan col (rdr: SqliteDataReader) =
        if rdr.IsDBNull(rdr.GetOrdinal col) then None else Some (getTimeSpan col rdr)
    
    /// Create a permalink from the current row in the given data reader
    let toPermalink rdr = getString "permalink" rdr |> Permalink
    
    /// Create a revision from the current row in the given data reader
    let toRevision rdr : Revision =
        { AsOf = getInstant "as_of"         rdr
          Text = getString  "revision_text" rdr |> MarkupText.Parse }
    
    /// Create a theme asset from the current row in the given data reader
    let toThemeAsset includeData rdr : ThemeAsset =
        let assetData =
            if includeData then
                use dataStream = new MemoryStream()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray()
            else
                [||]
        { Id        = ThemeAssetId (ThemeId (getString "theme_id" rdr), getString "path" rdr)
          UpdatedOn = getInstant "updated_on" rdr
          Data      = assetData }
    
    /// Create an uploaded file from the current row in the given data reader
    let toUpload includeData rdr : Upload =
        let data =
            if includeData then
                use dataStream = new MemoryStream ()
                use blobStream = getStream "data" rdr
                blobStream.CopyTo dataStream
                dataStream.ToArray ()
            else
                [||]
        { Id        = getString  "id"         rdr |> UploadId
          WebLogId  = getString  "web_log_id" rdr |> WebLogId
          Path      = getString  "path"       rdr |> Permalink
          UpdatedOn = getInstant "updated_on" rdr
          Data      = data }
    
    /// Map from a document to a domain type, specifying the field name for the document
    let fromData<'T> ser rdr fieldName : 'T =
        Utils.deserialize<'T> ser (getString fieldName rdr)
        
    /// Map from a document to a domain type
    let fromDoc<'T> ser rdr : 'T =
        fromData<'T> ser rdr "data"

/// Create a list of items for the results of the given command
let cmdToList<'TDoc> (cmd: SqliteCommand) ser = backgroundTask {
    use! rdr = cmd.ExecuteReaderAsync()
    let mutable it: 'TDoc list = []
    while! rdr.ReadAsync() do
        it <- Map.fromDoc ser rdr :: it
    return List.rev it
}

/// Queries to assist with document manipulation
[<Obsolete("change me")>]
module QueryOld =
    
    /// Fragment to add an ID condition to a WHERE clause (parameter @id)
    let whereById =
        "data ->> 'Id' = @id"
    
    /// Fragment to add a web log ID condition to a WHERE clause (parameter @webLogId)
    let whereByWebLog =
        "data ->> 'WebLogId' = @webLogId"
    
    /// A SELECT/FROM pair for the given table
    let selectFromTable table =
        $"SELECT data FROM %s{table}"
    
    /// An INSERT statement for a document (parameter @data)
    let insert table =
        $"INSERT INTO %s{table} VALUES (@data)"
    
    /// A SELECT query to count documents for a given web log ID
    let countByWebLog table =
        $"SELECT COUNT(*) FROM %s{table} WHERE {whereByWebLog}"
    
    /// An UPDATE query to update a full document by its ID (parameters @data and @id)
    let updateById table =
        $"UPDATE %s{table} SET data = @data WHERE {whereById}"
    
    /// A DELETE query to delete a document by its ID (parameter @id)
    let deleteById table =
        $"DELETE FROM %s{table} WHERE {whereById}"
    

/// Create a document ID parameter
let idParam (key: 'TKey) =
    SqliteParameter("@id", string key)

/// Create a web log ID parameter
let webLogParam (webLogId: WebLogId) =
    SqliteParameter("@webLogId", string webLogId)

let addParam (cmd: SqliteCommand) name (value: obj) =
    cmd.Parameters.AddWithValue(name, value) |> ignore

/// Add an ID parameter for a document
let addDocId<'TKey> (cmd: SqliteCommand) (id: 'TKey) =
    addParam cmd "@id" (string id)

/// Add a document parameter
let addDocParam<'TDoc> (cmd: SqliteCommand) (doc: 'TDoc) ser =
    addParam cmd "@data" (Utils.serialize ser doc)

/// Add a web log ID parameter
let addWebLogId (cmd: SqliteCommand) (webLogId: WebLogId) =
    addParam cmd "@webLogId" (string webLogId)

open BitBadger.Sqlite.FSharp.Documents
open BitBadger.Sqlite.FSharp.Documents.WithConn

/// Functions for manipulating documents
module Document =
    
    /// Queries to assist with document manipulation
    module Query =
        
        /// Fragment to add a web log ID condition to a WHERE clause (parameter @webLogId)
        let whereByWebLog =
            Query.whereFieldEquals "WebLogId" "@webLogId"
        
        /// A SELECT query to count documents for a given web log ID
        let countByWebLog table =
            $"{Query.Count.all table} WHERE {whereByWebLog}"
        
        /// A query to select from a table by the document's ID and its web log ID
        let selectByIdAndWebLog table =
            $"{Query.Find.byFieldEquals table} AND {whereByWebLog}"
        
        /// A query to select from a table by its web log ID
        let selectByWebLog table =
            $"{Query.selectFromTable table} WHERE {whereByWebLog}"
    
    /// Count documents for the given web log ID
    let countByWebLog table (webLogId: WebLogId) conn = backgroundTask {
        let! count = Count.byFieldEquals table "WebLogId" webLogId conn
        return int count
    }
    
    /// Find a document by its ID
    [<Obsolete("replace this")>]
    let findById<'TKey, 'TDoc> (conn: SqliteConnection) ser table (key: 'TKey) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.selectFromTable table} WHERE {Query.whereById}"
        addDocId cmd key
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.fromDoc<'TDoc> ser rdr) else None
    }
    
    /// Find a document by its ID and web log ID
    let findByIdAndWebLog<'TKey, 'TDoc> table (key: 'TKey) webLogId conn =
        Custom.single (Query.selectByIdAndWebLog table) [ idParam key; webLogParam webLogId ] fromData<'TDoc> conn
    
    /// Find documents for the given web log
    let findByWebLog<'TDoc> table (webLogId: WebLogId) conn =
        Find.byFieldEquals<'TDoc> table "WebLogId" webLogId conn
    
    /// Insert a document
    [<Obsolete("replace this")>]
    let insert<'TDoc> (conn: SqliteConnection) ser table (doc: 'TDoc) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- QueryOld.insert table
        addDocParam<'TDoc> cmd doc ser
        do! write cmd
    }
    
    /// Update (replace) a document by its ID
    [<Obsolete("replace this")>]
    let update<'TKey, 'TDoc> (conn: SqliteConnection) ser table (key: 'TKey) (doc: 'TDoc) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- QueryOld.updateById table
        addDocId cmd key
        addDocParam<'TDoc> cmd doc ser
        do! write cmd
    }
    
    /// Update a field in a document by its ID
    [<Obsolete("replace this")>]
    let updateField<'TKey, 'TValue> (conn: SqliteConnection) ser table (key: 'TKey) jsonField
            (value: 'TValue) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <-
            $"UPDATE %s{table} SET data = json_set(data, '$.{jsonField}', json(@it)) WHERE {QueryOld.whereById}"
        addDocId cmd key
        addParam cmd "@it" (Utils.serialize ser value)
        do! write cmd
    }
    
    /// Delete a document by its ID
    [<Obsolete("replace this")>]
    let delete<'TKey> (conn: SqliteConnection) table (key: 'TKey) = backgroundTask {
        use cmd = conn.CreateCommand()
        cmd.CommandText <- QueryOld.deleteById table
        addDocId cmd key
        do! write cmd
    }

/// Functions to support revisions
module Revisions =
    
    /// Find all revisions for the given entity
    let findByEntityId<'TKey> revTable entityTable (key: 'TKey) conn =
        Custom.list
            $"SELECT as_of, revision_text FROM %s{revTable} WHERE %s{entityTable}_id = @id ORDER BY as_of DESC"
            [ idParam key ]
            Map.toRevision
            conn
    
    /// Find all revisions for all posts for the given web log
    let findByWebLog<'TKey> revTable entityTable (keyFunc: string -> 'TKey) webLogId conn =
        Custom.list
            $"SELECT pr.*
                FROM %s{revTable} pr
                     INNER JOIN %s{entityTable} p ON p.data ->> 'Id' = pr.{entityTable}_id
               WHERE p.{Document.Query.whereByWebLog}
               ORDER BY as_of DESC"
            [ webLogParam webLogId ]
            (fun rdr -> keyFunc (Map.getString $"{entityTable}_id" rdr), Map.toRevision rdr)
            conn

    /// Parameters for a revision INSERT statement
    let revParams<'TKey> (key: 'TKey) rev =
        [ SqliteParameter("asOf",  rev.AsOf)
          SqliteParameter("@id",   string key)
          SqliteParameter("@text", rev.Text) ]
    
    /// Update a page or post's revisions
    let update<'TKey> revTable entityTable (key: 'TKey) oldRevs newRevs conn = backgroundTask {
        let toDelete, toAdd = Utils.diffRevisions oldRevs newRevs
        if not (List.isEmpty toDelete) || not (List.isEmpty toAdd) then
            for delRev in toDelete do
                do! Custom.nonQuery
                        $"DELETE FROM %s{revTable} WHERE %s{entityTable}_id = @id AND as_of = @asOf"
                        [ idParam key; SqliteParameter("@asOf", instantParam delRev.AsOf) ]
                        conn
            for addRev in toAdd do
                do! Custom.nonQuery $"INSERT INTO {revTable} VALUES (@id, @asOf, @text)" (revParams key addRev) conn
    }
