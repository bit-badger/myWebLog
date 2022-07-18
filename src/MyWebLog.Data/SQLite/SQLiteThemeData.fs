namespace MyWebLog.Data.SQLite

open System.Threading.Tasks
open Microsoft.Data.Sqlite
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog theme data implementation        
type SQLiteThemeData (conn : SqliteConnection) =
    
    /// Retrieve all themes (except 'admin'; excludes templates)
    let all () = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM theme WHERE id <> 'admin' ORDER BY id"
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList Map.toTheme rdr
    }
    
    /// Find a theme by its ID
    let findById themeId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT * FROM theme WHERE id = @id"
        cmd.Parameters.AddWithValue ("@id", ThemeId.toString themeId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        if rdr.Read () then
            let theme = Map.toTheme rdr
            let templateCmd = conn.CreateCommand ()
            templateCmd.CommandText <- "SELECT * FROM theme_template WHERE theme_id = @id"
            templateCmd.Parameters.Add cmd.Parameters["@id"] |> ignore
            use! templateRdr = templateCmd.ExecuteReaderAsync ()
            return Some { theme with templates = toList Map.toThemeTemplate templateRdr }
        else
            return None
    }
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText themeId = backgroundTask {
        match! findById themeId with
        | Some theme ->
            return Some {
                theme with templates = theme.templates |> List.map (fun t -> { t with text = "" })
            }
        | None -> return None
    }
    
    /// Save a theme
    let save (theme : Theme) = backgroundTask {
        use cmd = conn.CreateCommand ()
        let! oldTheme = findById theme.id
        cmd.CommandText <-
            match oldTheme with
            | Some _ -> "UPDATE theme SET name = @name, version = @version WHERE id = @id"
            | None -> "INSERT INTO theme VALUES (@id, @name, @version)"
        [ cmd.Parameters.AddWithValue ("@id", ThemeId.toString theme.id)
          cmd.Parameters.AddWithValue ("@name", theme.name)
          cmd.Parameters.AddWithValue ("@version", theme.version)
        ] |> ignore
        do! write cmd
        
        let toDelete, toAdd =
            diffLists (oldTheme |> Option.map (fun t -> t.templates) |> Option.defaultValue [])
                      theme.templates (fun t -> t.name)
        let toUpdate =
            theme.templates
            |> List.filter (fun t ->
                not (toDelete |> List.exists (fun d -> d.name = t.name))
                && not (toAdd |> List.exists (fun a -> a.name = t.name)))
        cmd.CommandText <-
            "UPDATE theme_template SET template = @template WHERE theme_id = @themeId AND name = @name"
        cmd.Parameters.Clear ()
        [ cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString theme.id)
          cmd.Parameters.Add ("@name", SqliteType.Text)
          cmd.Parameters.Add ("@template", SqliteType.Text)
        ] |> ignore
        toUpdate
        |> List.map (fun template -> backgroundTask {
            cmd.Parameters["@name"    ].Value <- template.name
            cmd.Parameters["@template"].Value <- template.text
            do! write cmd
        })
        |> Task.WhenAll
        |> ignore
        cmd.CommandText <- "INSERT INTO theme_template VALUES (@themeId, @name, @template)"
        toAdd
        |> List.map (fun template -> backgroundTask {
            cmd.Parameters["@name"    ].Value <- template.name
            cmd.Parameters["@template"].Value <- template.text
            do! write cmd
        })
        |> Task.WhenAll
        |> ignore
        cmd.CommandText <- "DELETE FROM theme_template WHERE theme_id = @themeId AND name = @name"
        cmd.Parameters.Remove cmd.Parameters["@template"]
        toDelete
        |> List.map (fun template -> backgroundTask {
            cmd.Parameters["@name"].Value <- template.name
            do! write cmd
        })
        |> Task.WhenAll
        |> ignore
    }
    
    interface IThemeData with
        member _.All () = all ()
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


open System.IO

/// SQLite myWebLog theme data implementation        
type SQLiteThemeAssetData (conn : SqliteConnection) =
    
    /// Get all theme assets (excludes data)
    let all () = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset"
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toThemeAsset false) rdr
    }
    
    /// Delete all assets for the given theme
    let deleteByTheme themeId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "DELETE FROM theme_asset WHERE theme_id = @themeId"
        cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
        do! write cmd
    }
    
    /// Find a theme asset by its ID
    let findById assetId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT *, ROWID FROM theme_asset WHERE theme_id = @themeId AND path = @path"
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        [ cmd.Parameters.AddWithValue ("@themeId", themeId)
          cmd.Parameters.AddWithValue ("@path", path)
        ] |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return if rdr.Read () then Some (Map.toThemeAsset true rdr) else None
    }
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme themeId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT theme_id, path, updated_on FROM theme_asset WHERE theme_id = @themeId"
        cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toThemeAsset false) rdr
    }
    
    /// Get theme assets for the given theme
    let findByThemeWithData themeId = backgroundTask {
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- "SELECT *, ROWID FROM theme_asset WHERE theme_id = @themeId"
        cmd.Parameters.AddWithValue ("@themeId", ThemeId.toString themeId) |> ignore
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toThemeAsset true) rdr
    }
    
    /// Save a theme asset
    let save (asset : ThemeAsset) = backgroundTask {
        use sideCmd = conn.CreateCommand ()
        sideCmd.CommandText <-
            "SELECT COUNT(path) FROM theme_asset WHERE theme_id = @themeId AND path = @path"
        let (ThemeAssetId (ThemeId themeId, path)) = asset.id
        [ sideCmd.Parameters.AddWithValue ("@themeId", themeId)
          sideCmd.Parameters.AddWithValue ("@path", path)
        ] |> ignore
        let! exists = count sideCmd
        
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            if exists = 1 then
                """UPDATE theme_asset
                      SET updated_on = @updatedOn,
                          data       = ZEROBLOB(@dataLength)
                    WHERE theme_id = @themeId
                      AND path     = @path"""
            else
                """INSERT INTO theme_asset (
                       theme_id, path, updated_on, data
                   ) VALUES (
                       @themeId, @path, @updatedOn, ZEROBLOB(@dataLength)
                   )"""
        [ cmd.Parameters.AddWithValue ("@themeId", themeId)
          cmd.Parameters.AddWithValue ("@path", path)
          cmd.Parameters.AddWithValue ("@updatedOn", asset.updatedOn)
          cmd.Parameters.AddWithValue ("@dataLength", asset.data.Length)
        ] |> ignore
        do! write cmd
        
        sideCmd.CommandText <- "SELECT ROWID FROM theme_asset WHERE theme_id = @themeId AND path = @path"
        let! rowId = sideCmd.ExecuteScalarAsync ()
        
        use dataStream = new MemoryStream (asset.data)
        use blobStream = new SqliteBlob (conn, "theme_asset", "data", rowId :?> int64)
        do! dataStream.CopyToAsync blobStream
    }
    
    interface IThemeAssetData with
        member _.All () = all ()
        member _.DeleteByTheme themeId = deleteByTheme themeId
        member _.FindById assetId = findById assetId
        member _.FindByTheme themeId = findByTheme themeId
        member _.FindByThemeWithData themeId = findByThemeWithData themeId
        member _.Save asset = save asset
