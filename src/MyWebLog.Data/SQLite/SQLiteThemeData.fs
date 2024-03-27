namespace MyWebLog.Data.SQLite

open BitBadger.Documents
open BitBadger.Documents.Sqlite
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data

/// SQLite myWebLog theme data implementation
type SQLiteThemeData(conn : SqliteConnection, log: ILogger) =
    
    /// The JSON field for the theme ID
    let idField = $"data ->> '{nameof Theme.Empty.Id}'"
    
    /// Convert a document to a theme with no template text
    let withoutTemplateText (rdr: SqliteDataReader) =
        let theme = fromData<Theme> rdr
        { theme with Templates = theme.Templates |> List.map (fun t -> { t with Text = "" })}
    
    /// Remove the template text from a theme
    let withoutTemplateText' (it: Theme) =
        { it with Templates = it.Templates |> List.map (fun t -> { t with Text = "" }) }
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () =
        log.LogTrace "Theme.all"
        conn.customList
            $"{Query.selectFromTable Table.Theme} WHERE {idField} <> 'admin' ORDER BY {idField}"
            []
            withoutTemplateText
    
    /// Does a given theme exist?
    let exists (themeId: ThemeId) =
        log.LogTrace "Theme.exists"
        conn.existsById Table.Theme themeId
    
    /// Find a theme by its ID
    let findById themeId =
        log.LogTrace "Theme.findById"
        conn.findById<ThemeId, Theme> Table.Theme themeId
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText (themeId: ThemeId) =
        log.LogTrace "Theme.findByIdWithoutText"
        conn.customSingle (Query.Find.byId Table.Theme) [ idParam themeId ] withoutTemplateText
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        log.LogTrace "Theme.delete"
        match! findByIdWithoutText themeId with
        | Some _ ->
            do! conn.customNonQuery
                    $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @id; {Query.Delete.byId Table.Theme}"
                    [ idParam themeId ]
            return true
        | None -> return false
    }
    
    /// Save a theme
    let save (theme: Theme) =
        log.LogTrace "Theme.save"
        conn.save Table.Theme theme
    
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
    
    /// Create parameters for a theme asset ID
    let assetIdParams assetId =
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        [ idParam themeId; sqlParam "@path" path ]
    
    /// Get all theme assets (excludes data)
    let all () =
        log.LogTrace "ThemeAsset.all"
        conn.customList $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset}" [] (Map.toThemeAsset false)
    
    /// Delete all assets for the given theme
    let deleteByTheme (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.deleteByTheme"
        conn.customNonQuery $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @id" [ idParam themeId ]
    
    /// Find a theme asset by its ID
    let findById assetId =
        log.LogTrace "ThemeAsset.findById"
        conn.customSingle
            $"SELECT *, ROWID FROM {Table.ThemeAsset} WHERE theme_id = @id AND path = @path"
            (assetIdParams assetId)
            (Map.toThemeAsset true)
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.findByTheme"
        conn.customList
            $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset} WHERE theme_id = @id"
            [ idParam themeId ]
            (Map.toThemeAsset false)
    
    /// Get theme assets for the given theme
    let findByThemeWithData (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.findByThemeWithData"
        conn.customList
            $"SELECT *, ROWID FROM {Table.ThemeAsset} WHERE theme_id = @id"
            [ idParam themeId ]
            (Map.toThemeAsset true)
    
    /// Save a theme asset
    let save (asset: ThemeAsset) = backgroundTask {
        log.LogTrace "ThemeAsset.save"
        do! conn.customNonQuery
                $"INSERT INTO {Table.ThemeAsset} (
                    theme_id, path, updated_on, data
                  ) VALUES (
                    @id, @path, @updatedOn, ZEROBLOB(@dataLength)
                  ) ON CONFLICT (theme_id, path) DO UPDATE
                  SET updated_on = @updatedOn,
                      data       = ZEROBLOB(@dataLength)"
                [ sqlParam "@updatedOn" (instantParam asset.UpdatedOn)
                  sqlParam "@dataLength" asset.Data.Length
                  yield! (assetIdParams asset.Id) ]
        
        let! rowId =
            conn.customScalar
                $"SELECT ROWID FROM {Table.ThemeAsset} WHERE theme_id = @id AND path = @path"
                (assetIdParams asset.Id)
                _.GetInt64(0)
        use dataStream = new MemoryStream(asset.Data)
        use blobStream = new SqliteBlob(conn, Table.ThemeAsset, "data", rowId)
        do! dataStream.CopyToAsync blobStream
    }
    
    interface IThemeAssetData with
        member _.All() = all ()
        member _.DeleteByTheme themeId = deleteByTheme themeId
        member _.FindById assetId = findById assetId
        member _.FindByTheme themeId = findByTheme themeId
        member _.FindByThemeWithData themeId = findByThemeWithData themeId
        member _.Save asset = save asset
