namespace MyWebLog.Data.Postgres

open Microsoft.Extensions.Logging
open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostreSQL myWebLog theme data implementation        
type PostgresThemeData (source : NpgsqlDataSource, log : ILogger) =
    
    /// Clear out the template text from a theme
    let withoutTemplateText row =
        let theme = fromData<Theme> row
        { theme with Templates = theme.Templates |> List.map (fun template -> { template with Text = "" }) }
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () =
        log.LogTrace "Theme.all"
        Sql.fromDataSource source
        |> Sql.query $"{Query.selectFromTable Table.Theme} WHERE id <> 'admin' ORDER BY id"
        |> Sql.executeAsync withoutTemplateText
    
    /// Does a given theme exist?
    let exists themeId =
        log.LogTrace "Theme.exists"
        Exists.byId Table.Theme (ThemeId.toString themeId)
    
    /// Find a theme by its ID
    let findById themeId =
        log.LogTrace "Theme.findById"
        Find.byId<Theme> Table.Theme (ThemeId.toString themeId)
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText themeId =
        log.LogTrace "Theme.findByIdWithoutText"
        Sql.fromDataSource source
        |> Sql.query $"{Query.selectFromTable Table.Theme} WHERE id = @id"
        |> Sql.parameters [ "@id", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync withoutTemplateText
        |> tryHead
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        log.LogTrace "Theme.delete"
        match! exists themeId with
        | true ->
            do! Delete.byId Table.Theme (ThemeId.toString themeId)
            return true
        | false -> return false
    }
    
    /// Save a theme
    let save (theme : Theme) =
        log.LogTrace "Theme.save"
        save Table.Theme (ThemeId.toString theme.Id) theme
    
    interface IThemeData with
        member _.All () = all ()
        member _.Delete themeId = delete themeId
        member _.Exists themeId = exists themeId
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


/// PostreSQL myWebLog theme data implementation        
type PostgresThemeAssetData (source : NpgsqlDataSource, log : ILogger) =
    
    /// Get all theme assets (excludes data)
    let all () =
        log.LogTrace "ThemeAsset.all"
        Sql.fromDataSource source
        |> Sql.query $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset}"
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Delete all assets for the given theme
    let deleteByTheme themeId = backgroundTask {
        log.LogTrace "ThemeAsset.deleteByTheme"
        let! _ =
            Sql.fromDataSource source
            |> Sql.query $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
            |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a theme asset by its ID
    let findById assetId =
        log.LogTrace "ThemeAsset.findById"
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
        |> Sql.parameters [ "@themeId", Sql.string themeId; "@path", Sql.string path ]
        |> Sql.executeAsync (Map.toThemeAsset true)
        |> tryHead
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme themeId =
        log.LogTrace "ThemeAsset.findByTheme"
        Sql.fromDataSource source
        |> Sql.query $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Get theme assets for the given theme
    let findByThemeWithData themeId =
        log.LogTrace "ThemeAsset.findByThemeWithData"
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset true)
    
    /// Save a theme asset
    let save (asset : ThemeAsset) = backgroundTask {
        log.LogTrace "ThemeAsset.save"
        let (ThemeAssetId (ThemeId themeId, path)) = asset.Id
        let! _ =
            Sql.fromDataSource source
            |> Sql.query $"
                INSERT INTO {Table.ThemeAsset} (
                    theme_id, path, updated_on, data
                ) VALUES (
                    @themeId, @path, @updatedOn, @data
                ) ON CONFLICT (theme_id, path) DO UPDATE
                SET updated_on = EXCLUDED.updated_on,
                    data       = EXCLUDED.data"
            |> Sql.parameters
                [   "@themeId", Sql.string themeId
                    "@path",    Sql.string path
                    "@data",    Sql.bytea  asset.Data
                    typedParam "updatedOn" asset.UpdatedOn ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    interface IThemeAssetData with
        member _.All () = all ()
        member _.DeleteByTheme themeId = deleteByTheme themeId
        member _.FindById assetId = findById assetId
        member _.FindByTheme themeId = findByTheme themeId
        member _.FindByThemeWithData themeId = findByThemeWithData themeId
        member _.Save asset = save asset
