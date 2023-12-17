namespace MyWebLog.Data.Postgres

open BitBadger.Npgsql.FSharp.Documents
open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql.FSharp

/// PostreSQL myWebLog theme data implementation
type PostgresThemeData(log: ILogger) =
    
    /// Clear out the template text from a theme
    let withoutTemplateText row =
        let theme = fromData<Theme> row
        { theme with Templates = theme.Templates |> List.map (fun template -> { template with Text = "" }) }
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () =
        log.LogTrace "Theme.all"
        Custom.list
            $"{Query.selectFromTable Table.Theme} WHERE data ->> '{nameof Theme.Empty.Id}' <> 'admin' ORDER BY id"
            []
            withoutTemplateText
    
    /// Does a given theme exist?
    let exists (themeId: ThemeId) =
        log.LogTrace "Theme.exists"
        Exists.byId Table.Theme (string themeId)
    
    /// Find a theme by its ID
    let findById (themeId: ThemeId) =
        log.LogTrace "Theme.findById"
        Find.byId<Theme> Table.Theme (string themeId)
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText (themeId: ThemeId) =
        log.LogTrace "Theme.findByIdWithoutText"
        Custom.single (Query.Find.byId Table.Theme) [ "@id", Sql.string (string themeId) ] withoutTemplateText
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        log.LogTrace "Theme.delete"
        match! exists themeId with
        | true ->
            do! Custom.nonQuery
                    $"""DELETE FROM {Table.ThemeAsset} WHERE theme_id = @id;
                        DELETE FROM {Table.Theme}      WHERE {Query.whereById "@id"}"""
                    [ "@id", Sql.string (string themeId) ]
            return true
        | false -> return false
    }
    
    /// Save a theme
    let save (theme: Theme) =
        log.LogTrace "Theme.save"
        save Table.Theme theme
    
    interface IThemeData with
        member _.All() = all ()
        member _.Delete themeId = delete themeId
        member _.Exists themeId = exists themeId
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


/// PostreSQL myWebLog theme data implementation
type PostgresThemeAssetData(log: ILogger) =
    
    /// Get all theme assets (excludes data)
    let all () =
        log.LogTrace "ThemeAsset.all"
        Custom.list $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset}" [] (Map.toThemeAsset false)
    
    /// Delete all assets for the given theme
    let deleteByTheme (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.deleteByTheme"
        Custom.nonQuery $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @id" [ "@id", Sql.string (string themeId) ]
    
    /// Find a theme asset by its ID
    let findById assetId =
        log.LogTrace "ThemeAsset.findById"
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        Custom.single
            $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
            [ "@themeId", Sql.string themeId; "@path", Sql.string path ]
            (Map.toThemeAsset true)
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.findByTheme"
        Custom.list
            $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
            [ "@themeId", Sql.string (string themeId) ]
            (Map.toThemeAsset false)
    
    /// Get theme assets for the given theme
    let findByThemeWithData (themeId: ThemeId) =
        log.LogTrace "ThemeAsset.findByThemeWithData"
        Custom.list
            $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
            [ "@themeId", Sql.string (string themeId) ]
            (Map.toThemeAsset true)
    
    /// Save a theme asset
    let save (asset: ThemeAsset) =
        log.LogTrace "ThemeAsset.save"
        let (ThemeAssetId (ThemeId themeId, path)) = asset.Id
        Custom.nonQuery
            $"INSERT INTO {Table.ThemeAsset} (
                  theme_id, path, updated_on, data
              ) VALUES (
                  @themeId, @path, @updatedOn, @data
              ) ON CONFLICT (theme_id, path) DO UPDATE
              SET updated_on = EXCLUDED.updated_on,
                  data       = EXCLUDED.data"
            [ "@themeId", Sql.string themeId
              "@path",    Sql.string path
              "@data",    Sql.bytea  asset.Data
              typedParam "updatedOn" asset.UpdatedOn ]
    
    interface IThemeAssetData with
        member _.All() = all ()
        member _.DeleteByTheme themeId = deleteByTheme themeId
        member _.FindById assetId = findById assetId
        member _.FindByTheme themeId = findByTheme themeId
        member _.FindByThemeWithData themeId = findByThemeWithData themeId
        member _.Save asset = save asset
