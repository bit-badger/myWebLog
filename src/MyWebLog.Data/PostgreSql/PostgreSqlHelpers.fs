/// Helper functions for the PostgreSQL data implementation
[<AutoOpen>]
module MyWebLog.Data.PostgreSql.PostgreSqlHelpers

open MyWebLog
open Npgsql.FSharp

/// Create a SQL parameter for the web log ID
let webLogIdParam webLogId =
    "@webLogId", Sql.string (WebLogId.toString webLogId)

/// Mapping functions for SQL queries
module Map =
    
    /// Create a category from the current row in the given data reader
    let toCategory (row : RowReader) : Category =
        {   Id          = row.string       "id"         |> CategoryId
            WebLogId    = row.string       "web_log_id" |> WebLogId
            Name        = row.string       "name"
            Slug        = row.string       "slug"
            Description = row.stringOrNone "description"
            ParentId    = row.stringOrNone "parent_id"  |> Option.map CategoryId
        }

    /// Get a count from a row
    let toCount (row : RowReader) =
        row.int "the_count"
