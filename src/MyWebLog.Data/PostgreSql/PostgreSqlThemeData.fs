namespace MyWebLog.Data.PostgreSql

open MyWebLog
open MyWebLog.Data
open Npgsql
open Npgsql.FSharp

/// PostreSQL myWebLog theme data implementation        
type PostgreSqlThemeData (conn : NpgsqlConnection) =
    
    /// Retrieve all themes (except 'admin'; excludes template text)
    let all () = backgroundTask {
        let! themes =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM theme WHERE id <> 'admin' ORDER BY id"
            |> Sql.executeAsync Map.toTheme
        let! templates =
            Sql.existingConnection conn
            |> Sql.query "SELECT name, theme_id FROM theme_template WHERE theme_id <> 'admin' ORDER BY name"
            |> Sql.executeAsync (fun row -> ThemeId (row.string "theme_id"), Map.toThemeTemplate false row)
        return
            themes
            |> List.map (fun t ->
                { t with Templates = templates |> List.filter (fun tt -> fst tt = t.Id) |> List.map snd })
    }
    
    /// Does a given theme exist?
    let exists themeId =
        Sql.existingConnection conn
        |> Sql.query "SELECT EXISTS (SELECT 1 FROM theme WHERE id = @id) AS does_exist"
        |> Sql.parameters [ "@id", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeRowAsync Map.toExists
    
    /// Find a theme by its ID
    let findById themeId = backgroundTask {
        let themeIdParam = [ "@id", Sql.string (ThemeId.toString themeId) ]
        let! tryTheme =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM theme WHERE id = @id"
            |> Sql.parameters themeIdParam
            |> Sql.executeAsync Map.toTheme
        match List.tryHead tryTheme with
        | Some theme ->
            let! templates =
                Sql.existingConnection conn
                |> Sql.query "SELECT * FROM theme_template WHERE theme_id = @id"
                |> Sql.parameters themeIdParam
                |> Sql.executeAsync (Map.toThemeTemplate true)
            return Some { theme with Templates = templates }
        | None -> return None
    }
    
    /// Find a theme by its ID (excludes the text of templates)
    let findByIdWithoutText themeId = backgroundTask {
        match! findById themeId with
        | Some theme ->
            return Some {
                theme with Templates = theme.Templates |> List.map (fun t -> { t with Text = "" })
            }
        | None -> return None
    }
    
    /// Delete a theme by its ID
    let delete themeId = backgroundTask {
        match! findByIdWithoutText themeId with
        | Some _ ->
            let! _ =
                Sql.existingConnection conn
                |> Sql.query """
                    DELETE FROM theme_asset    WHERE theme_id = @id;
                    DELETE FROM theme_template WHERE theme_id = @id;
                    DELETE FROM theme          WHERE id       = @id"""
                |> Sql.parameters [ "@id", Sql.string (ThemeId.toString themeId) ]
                |> Sql.executeNonQueryAsync
            return true
        | None -> return false
    }
    
    /// Save a theme
    let save (theme : Theme) = backgroundTask {
        let! oldTheme     = findById theme.Id
        let  themeIdParam = Sql.string (ThemeId.toString theme.Id)
        let! _ =
            Sql.existingConnection conn
            |> Sql.query """
                INSERT INTO theme VALUES (@id, @name, @version)
                ON CONFLICT (id) DO UPDATE
                SET name    = EXCLUDED.name,
                    version = EXCLUDED.version"""
            |> Sql.parameters
                [   "@id",      themeIdParam
                    "@name",    Sql.string theme.Name
                    "@version", Sql.string theme.Version ]
            |> Sql.executeNonQueryAsync
        
        let toDelete, _ =
            Utils.diffLists (oldTheme |> Option.map (fun t -> t.Templates) |> Option.defaultValue [])
                            theme.Templates (fun t -> t.Name)
        let toAddOrUpdate =
            theme.Templates
            |> List.filter (fun t -> not (toDelete |> List.exists (fun d -> d.Name = t.Name)))
        
        if not (List.isEmpty toDelete) || not (List.isEmpty toAddOrUpdate) then
            let! _ =
                Sql.existingConnection conn
                |> Sql.executeTransactionAsync [
                    if not (List.isEmpty toDelete) then
                        "DELETE FROM theme_template WHERE theme_id = @themeId AND name = @name",
                        toDelete |> List.map (fun tmpl -> [ "@themeId", themeIdParam; "@name", Sql.string tmpl.Name ])
                    if not (List.isEmpty toAddOrUpdate) then
                        """INSERT INTO theme_template VALUES (@themeId, @name, @template)
                            ON CONFLICT (theme_id, name) DO UPDATE
                            SET template = EXCLUDED.template""",
                        toAddOrUpdate |> List.map (fun tmpl -> [
                            "@themeId",  themeIdParam
                            "@name",     Sql.string tmpl.Name
                            "@template", Sql.string tmpl.Text
                        ])
                ]
            ()
    }
    
    interface IThemeData with
        member _.All () = all ()
        member _.Delete themeId = delete themeId
        member _.Exists themeId = exists themeId
        member _.FindById themeId = findById themeId
        member _.FindByIdWithoutText themeId = findByIdWithoutText themeId
        member _.Save theme = save theme


/// PostreSQL myWebLog theme data implementation        
type PostgreSqlThemeAssetData (conn : NpgsqlConnection) =
    
    /// Get all theme assets (excludes data)
    let all () =
        Sql.existingConnection conn
        |> Sql.query "SELECT theme_id, path, updated_on FROM theme_asset"
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Delete all assets for the given theme
    let deleteByTheme themeId = backgroundTask {
        let! _ =
            Sql.existingConnection conn
            |> Sql.query "DELETE FROM theme_asset WHERE theme_id = @themeId"
            |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
            |> Sql.executeNonQueryAsync
        ()
    }
    
    /// Find a theme asset by its ID
    let findById assetId = backgroundTask {
        let (ThemeAssetId (ThemeId themeId, path)) = assetId
        let! asset =
            Sql.existingConnection conn
            |> Sql.query "SELECT * FROM theme_asset WHERE theme_id = @themeId AND path = @path"
            |> Sql.parameters [ "@themeId", Sql.string themeId; "@path", Sql.string path ]
            |> Sql.executeAsync (Map.toThemeAsset true)
        return List.tryHead asset
    }
    
    /// Get theme assets for the given theme (excludes data)
    let findByTheme themeId =
        Sql.existingConnection conn
        |> Sql.query "SELECT theme_id, path, updated_on FROM theme_asset WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset false)
    
    /// Get theme assets for the given theme
    let findByThemeWithData themeId =
        Sql.existingConnection conn
        |> Sql.query "SELECT * FROM theme_asset WHERE theme_id = @themeId"
        |> Sql.parameters [ "@themeId", Sql.string (ThemeId.toString themeId) ]
        |> Sql.executeAsync (Map.toThemeAsset true)
    
    /// Save a theme asset
    let save (asset : ThemeAsset) = backgroundTask {
        let (ThemeAssetId (ThemeId themeId, path)) = asset.Id
        let! _ =
            Sql.existingConnection conn
            |> Sql.query """
                INSERT INTO theme_asset (
                    theme_id, path, updated_on, data
                ) VALUES (
                    @themeId, @path, @updatedOn, @data
                ) ON CONFLICT (theme_id, path) DO UPDATE
                SET updated_on = EXCLUDED.updated_on,
                    data       = EXCLUDED.data"""
            |> Sql.parameters
                [   "@themeId",   Sql.string      themeId
                    "@path",      Sql.string      path
                    "@updatedOn", Sql.timestamptz asset.UpdatedOn
                    "@data",      Sql.bytea       asset.Data ]
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
