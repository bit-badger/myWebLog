namespace MyWebLog.Data.SQLite

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json

/// SQLite myWebLog theme data implementation
type SQLiteThemeData(conn : SqliteConnection, ser: JsonSerializer, log: ILogger) =
    
    /// The JSON field for the theme ID
    let idField = $"data ->> '{nameof Theme.Empty.Id}'"
    
    /// Remove the template text from a theme
    let withoutTemplateText (it: Theme) =
        { it with Templates = it.Templates |> List.map (fun t -> { t with Text = "" }) }
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () = backgroundTask {
        log.LogTrace "Theme.all"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"{Query.selectFromTable Table.Theme} WHERE {idField} <> 'admin' ORDER BY {idField}"
        let! themes = cmdToList<Theme> cmd ser
        return themes |> List.map withoutTemplateText
    }
    
    /// Does a given theme exist?
    let exists (themeId: ThemeId) = backgroundTask {
        log.LogTrace "Theme.exists"
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"SELECT COUNT(*) FROM {Table.Theme} WHERE {idField} = @id"
        addDocId cmd themeId
        let! count = count cmd
        return count > 0
    }
    
    /// Find a theme by its ID
    let findById themeId =
        log.LogTrace "Theme.findById"
        Document.findById<ThemeId, Theme> conn ser Table.Theme themeId
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText themeId = backgroundTask {
        log.LogTrace "Theme.findByIdWithoutText"
        let! theme = findById themeId
        return theme |> Option.map withoutTemplateText
    }
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        log.LogTrace "Theme.delete"
        match! findByIdWithoutText themeId with
        | Some _ ->
            use cmd = conn.CreateCommand()
            cmd.CommandText <- $"
                DELETE FROM {Table.ThemeAsset} WHERE theme_id = @id;
                DELETE FROM {Table.Theme}      WHERE {Query.whereById}"
            addDocId cmd themeId
            do! write cmd
            return true
        | None -> return false
    }
    
    /// Save a theme
    let save (theme: Theme) = backgroundTask {
        log.LogTrace "Theme.save"
        match! findById theme.Id with
        | Some _ -> do! Document.update conn ser Table.Theme theme.Id theme
        | None -> do! Document.insert conn ser Table.Theme theme
    }
    
    interface IThemeData with
        member _.All() = all ()
        member _.Delete themeId = delete themeId
        member _.Exists themeId = exists themeId
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


open System.IO

/// SQLite myWebLog theme data implementation
type SQLiteThemeAssetData(conn : SqliteConnection, log: ILogger) =
    
    /// Get all theme assets (excludes data)
    let all () = backgroundTask {
        log.LogTrace "ThemeAsset.all"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset}"
        use! rdr = cmd.ExecuteReaderAsync()
        return toList (Map.toThemeAsset false) rdr
    }
    
    /// Delete all assets for the given theme
    let deleteByTheme (themeId: ThemeId) = backgroundTask {
        log.LogTrace "ThemeAsset.deleteByTheme"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        addParam cmd "@themeId" (string themeId)
        do! write cmd
    }
    
    /// Find a theme asset by its ID
    let findById assetId = backgroundTask {
        log.LogTrace "ThemeAsset.findById"
        use cmd = conn.CreateCommand ()
        cmd.CommandText <- $"SELECT *, ROWID FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        addParam cmd "@themeId" themeId
        addParam cmd "@path"    path
        use! rdr = cmd.ExecuteReaderAsync()
        let! isFound = rdr.ReadAsync()
        return if isFound then Some (Map.toThemeAsset true rdr) else None
    }
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme (themeId: ThemeId) = backgroundTask {
        log.LogTrace "ThemeAsset.findByTheme"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        addParam cmd "@themeId" (string themeId)
        use! rdr = cmd.ExecuteReaderAsync()
        return toList (Map.toThemeAsset false) rdr
    }
    
    /// Get theme assets for the given theme
    let findByThemeWithData (themeId: ThemeId) = backgroundTask {
        log.LogTrace "ThemeAsset.findByThemeWithData"
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT *, ROWID FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        addParam cmd "@themeId" (string themeId)
        use! rdr = cmd.ExecuteReaderAsync ()
        return toList (Map.toThemeAsset true) rdr
    }
    
    /// Save a theme asset
    let save (asset: ThemeAsset) = backgroundTask {
        log.LogTrace "ThemeAsset.save"
        use sideCmd = conn.CreateCommand()
        sideCmd.CommandText <- $"SELECT COUNT(*) FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
        let (ThemeAssetId (ThemeId themeId, path)) = asset.Id
        addParam sideCmd "@themeId" themeId
        addParam sideCmd "@path"    path
        let! exists = count sideCmd
        
        use cmd = conn.CreateCommand ()
        cmd.CommandText <-
            if exists = 1 then
                $"UPDATE {Table.ThemeAsset}
                     SET updated_on = @updatedOn,
                         data       = ZEROBLOB(@dataLength)
                   WHERE theme_id = @themeId
                     AND path     = @path"
            else
                $"INSERT INTO {Table.ThemeAsset} (
                    theme_id, path, updated_on, data
                  ) VALUES (
                    @themeId, @path, @updatedOn, ZEROBLOB(@dataLength)
                  )"
        addParam cmd "@themeId"    themeId
        addParam cmd "@path"       path
        addParam cmd "@updatedOn"  (instantParam asset.UpdatedOn)
        addParam cmd "@dataLength" asset.Data.Length
        do! write cmd
        
        sideCmd.CommandText <- $"SELECT ROWID FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
        let! rowId = sideCmd.ExecuteScalarAsync()
        
        use dataStream = new MemoryStream(asset.Data)
        use blobStream = new SqliteBlob(conn, Table.ThemeAsset, "data", rowId :?> int64)
        do! dataStream.CopyToAsync blobStream
    }
    
    interface IThemeAssetData with
        member _.All() = all ()
        member _.DeleteByTheme themeId = deleteByTheme themeId
        member _.FindById assetId = findById assetId
        member _.FindByTheme themeId = findByTheme themeId
        member _.FindByThemeWithData themeId = findByThemeWithData themeId
        member _.Save asset = save asset
