namespace MyWebLog.Data.Postgres

open MyWebLog
open MyWebLog.Data
open Newtonsoft.Json
open Npgsql
open Npgsql.FSharp
open Npgsql.FSharp.Documents

/// PostreSQL myWebLog theme data implementation        
type PostgresThemeData (source : NpgsqlDataSource) =
    
    /// Clear out the template text from a theme
    let withoutTemplateText row =
        let theme = fromData<Theme> row
        { theme with Templates = theme.Templates |> List.map (fun template -> { template with Text = "" }) }
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () =
        Sql.fromDataSource source
        |> Sql.query $"{Query.selectFromTable Table.Theme} WHERE id <> 'admin' ORDER BY id"
        |> Sql.executeAsync withoutTemplateText
    
    /// Does a given theme exist?
    let exists themeId =
        Sql.fromDataSource source
        |> Query.existsById Table.Theme (ThemeId.toString themeId)
    
    /// Find a theme by its ID
    let findById themeId =
        Sql.fromDataSource source
        |> Query.tryById<Theme> Table.Theme (ThemeId.toString themeId)
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText themeId =
        Sql.fromDataSource source
        |> Sql.query $"{Query.selectFromTable Table.Theme} WHERE id = @id"
        |> Sql.parameters [ "@id", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync withoutTemplateText
        |> tryHead
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        match! exists themeId with
        | true ->
            do! Sql.fromDataSource source |> Query.deleteById Table.Theme (ThemeId.toString themeId)
            return true
        | false -> return false
    }
    
    /// Save a theme
    let save (theme : Theme) =
        Sql.fromDataSource source |> Query.save Table.Theme (ThemeId.toString theme.Id) theme
    
    interface IThemeData with
        member _.All () = all ()
        member _.Delete themeId = delete themeId
        member _.Exists themeId = exists themeId
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


/// PostreSQL myWebLog theme data implementation        
type PostgresThemeAssetData (source : NpgsqlDataSource) =
    
    /// Get all theme assets (excludes data)
    let all () =
        Sql.fromDataSource source
        |> Sql.query $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset}"
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Delete all assets for the given theme
    let deleteByTheme themeId = backgroundTask {
        let! _ =
            Sql.fromDataSource source
            |> Sql.query $"DELETE FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
            |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a theme asset by its ID
    let findById assetId =
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId AND path = @path"
        |> Sql.parameters [ "@themeId", Sql.string themeId; "@path", Sql.string path ]
        |> Sql.executeAsync (Map.toThemeAsset true)
        |> tryHead
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme themeId =
        Sql.fromDataSource source
        |> Sql.query $"SELECT theme_id, path, updated_on FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Get theme assets for the given theme
    let findByThemeWithData themeId =
        Sql.fromDataSource source
        |> Sql.query $"SELECT * FROM {Table.ThemeAsset} WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset true)
    
    /// Save a theme asset
    let save (asset : ThemeAsset) = backgroundTask {
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
