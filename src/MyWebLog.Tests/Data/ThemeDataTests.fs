/// <summary>
/// Integration tests for <see cref="IThemeData" /> implementations
/// </summary> 
module ThemeDataTests

open System.IO
open Expecto
open MyWebLog
open MyWebLog.Data
open NodaTime

/// The ID of the default theme (restored from root-weblog.json)
let private defaultId = ThemeId "default"

/// The ID of the test theme loaded and manipulated by these tests
let private testId = ThemeId "test-theme"

/// The dark version of the myWebLog logo
let private darkFile = File.ReadAllBytes "../admin-theme/wwwroot/logo-dark.png"

/// The light version of the myWebLog logo
let private lightFile = File.ReadAllBytes "../admin-theme/wwwroot/logo-light.png"

/// Ensure that theme templates do not have any text
let private ensureNoText theme =
    for template in theme.Templates do
        Expect.equal template.Text "" $"Text for template {template.Name} should have been blank"

let ``All succeeds`` (data: IData) = task {
    let! themes = data.Theme.All()
    Expect.hasLength themes 1 "There should have been one theme returned"
    Expect.equal themes[0].Id defaultId "ID was incorrect"
    Expect.equal themes[0].Name "myWebLog Default Theme" "Name was incorrect"
    Expect.equal themes[0].Version "2.1.0" "Version was incorrect"
    ensureNoText themes[0]
}

let ``Exists succeeds when the theme exists`` (data: IData) = task {
    let! exists = data.Theme.Exists defaultId
    Expect.isTrue exists "The \"default\" theme should have existed"
}

let ``Exists succeeds when the theme does not exist`` (data: IData) = task {
    let! exists = data.Theme.Exists (ThemeId "fancy")
    Expect.isFalse exists "The \"fancy\" theme should not have existed"
}

let ``FindById succeeds when the theme exists`` (data: IData) = task {
    let! theme = data.Theme.FindById defaultId
    Expect.isSome theme "The theme should have been found"
    let it = theme.Value
    Expect.equal it.Id defaultId "ID was incorrect"
    Expect.equal it.Name "myWebLog Default Theme" "Name was incorrect"
    Expect.equal it.Version "2.1.0" "Version was incorrect"
    for template in it.Templates do
        Expect.isNotEmpty template.Text $"Text for template {template.Name} should not have been blank"
}

let ``FindById succeeds when the theme does not exist`` (data: IData) = task {
    let! theme = data.Theme.FindById (ThemeId "missing")
    Expect.isNone theme "There should not have been a theme found"
}

let ``FindByIdWithoutText succeeds when the theme exists`` (data: IData) = task {
    let! theme = data.Theme.FindByIdWithoutText defaultId
    Expect.isSome theme "The theme should have been found"
    let it = theme.Value
    Expect.equal it.Id defaultId "ID was incorrect"
    ensureNoText it
}

let ``FindByIdWithoutText succeeds when the theme does not exist`` (data: IData) = task {
    let! theme = data.Theme.FindByIdWithoutText (ThemeId "ornate")
    Expect.isNone theme "There should not have been a theme found"
}

let ``Save succeeds when adding a theme`` (data: IData) = task {
    do! data.Theme.Save
            { Id        = testId
              Name      = "Test Theme"
              Version   = "evergreen"
              Templates =
                  [ { Name = "index"; Text = "<h1>{{ values_here }}</h1>" }
                    { Name = "single-post"; Text = "<p>{{ the_post }}" } ] }
    let! saved = data.Theme.FindById testId
    Expect.isSome saved "There should have been a theme returned"
    let it = saved.Value
    Expect.equal it.Id testId "ID was incorrect"
    Expect.equal it.Name "Test Theme" "Name was incorrect"
    Expect.equal it.Version "evergreen" "Version was incorrect"
    Expect.hasLength it.Templates 2 "There should have been 2 templates"
    Expect.equal it.Templates[0].Name "index" "Template 0 name incorrect"
    Expect.equal it.Templates[0].Text "<h1>{{ values_here }}</h1>" "Template 0 text incorrect"
    Expect.equal it.Templates[1].Name "single-post" "Template 1 name incorrect"
    Expect.equal it.Templates[1].Text "<p>{{ the_post }}" "Template 1 text incorrect"
}

let ``Save succeeds when updating a theme`` (data: IData) = task {
    do! data.Theme.Save
            { Id        = testId
              Name      = "Updated Theme"
              Version   = "still evergreen"
              Templates =
                  [ { Name = "index"; Text = "<h1>{{ values_there }}</h1>" }
                    { Name = "layout"; Text = "<!DOCTYPE html><etc />" }
                    { Name = "single-post"; Text = "<p>{{ the_post }}" } ] }
    let! updated = data.Theme.FindById testId
    Expect.isSome updated "The updated theme should have been returned"
    let it = updated.Value
    Expect.equal it.Id testId "ID was incorrect"
    Expect.equal it.Name "Updated Theme" "Name was incorrect"
    Expect.equal it.Version "still evergreen" "Version was incorrect"
    Expect.hasLength it.Templates 3 "There should have been 3 templates"
    Expect.equal it.Templates[0].Name "index" "Template 0 name incorrect"
    Expect.equal it.Templates[0].Text "<h1>{{ values_there }}</h1>" "Template 0 text incorrect"
    Expect.equal it.Templates[1].Name "layout" "Template 1 name incorrect"
    Expect.equal it.Templates[1].Text "<!DOCTYPE html><etc />" "Template 1 text incorrect"
    Expect.equal it.Templates[2].Name "single-post" "Template 2 name incorrect"
    Expect.equal it.Templates[2].Text "<p>{{ the_post }}" "Template 2 text incorrect"
}

let ``Delete succeeds when a theme is deleted`` (data: IData) = task {
    // Delete should also delete assets associated with the theme
    do! data.ThemeAsset.Save { Id = ThemeAssetId (testId, "logo-dark.png");  UpdatedOn = Noda.epoch; Data = darkFile  }
    do! data.ThemeAsset.Save { Id = ThemeAssetId (testId, "logo-light.png"); UpdatedOn = Noda.epoch; Data = lightFile }
    let! deleted = data.Theme.Delete testId
    Expect.isTrue deleted "The theme should have been deleted"
    let! assets = data.ThemeAsset.FindByTheme testId
    Expect.isEmpty assets "The theme's assets should have been deleted"
}

let ``Delete succeeds when a theme is not deleted`` (data: IData) = task {
    let! deleted = data.Theme.Delete (ThemeId "test-theme") // already deleted above
    Expect.isFalse deleted "The theme should not have been deleted"
}

/// <summary>
/// Integration tests for <see cref="IThemeAssetData" /> implementations
/// </summary> 
module Asset =
    
    /// The theme ID for which assets will be tested
    let private assetThemeId = ThemeId "asset-test"
    
    /// The asset ID for the dark logo
    let private darkId = ThemeAssetId (assetThemeId, "logo-dark.png")
    
    /// The asset ID for the light logo
    let private lightId = ThemeAssetId (assetThemeId, "logo-light.png")
    
    let ``Save succeeds when adding an asset`` (data: IData) = task {
        do! data.Theme.Save { Theme.Empty with Id = assetThemeId }
        do! data.ThemeAsset.Save { Id = lightId; UpdatedOn = Noda.epoch + Duration.FromDays 18; Data = lightFile }
        let! asset = data.ThemeAsset.FindById lightId
        Expect.isSome asset "The asset should have been found"
        let it = asset.Value
        Expect.equal it.Id lightId "ID was incorrect"
        Expect.equal it.UpdatedOn (Noda.epoch + Duration.FromDays 18) "Updated on was incorrect"
        Expect.equal it.Data lightFile "Data was incorrect"
    }
    
    let ``Save succeeds when updating an asset`` (data: IData) = task {
        do! data.ThemeAsset.Save { Id = lightId; UpdatedOn = Noda.epoch + Duration.FromDays 20; Data = darkFile }
        let! asset = data.ThemeAsset.FindById lightId
        Expect.isSome asset "The asset should have been found"
        let it = asset.Value
        Expect.equal it.Id lightId "ID was incorrect"
        Expect.equal it.UpdatedOn (Noda.epoch + Duration.FromDays 20) "Updated on was incorrect"
        Expect.equal it.Data darkFile "Data was incorrect"
    }
    
    let ``All succeeds`` (data: IData) = task {
        let! all = data.ThemeAsset.All()
        Expect.hasLength all 2 "There should have been 2 assets retrieved"
        for asset in all do
            Expect.contains
                [ ThemeAssetId (defaultId, "style.css"); lightId ] asset.Id $"Unexpected asset found ({asset.Id})"
            Expect.isEmpty asset.Data $"Asset {asset.Id} should not have had data"
    }
    
    let ``FindById succeeds when an asset is found`` (data: IData) = task {
        let! asset = data.ThemeAsset.FindById lightId
        Expect.isSome asset "The asset should have been found"
        let it = asset.Value
        Expect.equal it.Id lightId "ID was incorrect"
        Expect.equal it.UpdatedOn (Noda.epoch + Duration.FromDays 20) "Updated on was incorrect"
        Expect.equal it.Data darkFile "Data was incorrect"
    }
    
    let ``FindById succeeds when an asset is not found`` (data: IData) = task {
        let! asset = data.ThemeAsset.FindById (ThemeAssetId (assetThemeId, "404.jpg"))
        Expect.isNone asset "There should not have been an asset returned"
    }
    
    let ``FindByTheme succeeds when assets exist`` (data: IData) = task {
        do! data.ThemeAsset.Save { Id = darkId;  UpdatedOn = Noda.epoch; Data = darkFile  }
        do! data.ThemeAsset.Save { Id = lightId; UpdatedOn = Noda.epoch; Data = lightFile }
        let! assets = data.ThemeAsset.FindByTheme assetThemeId
        Expect.hasLength assets 2 "There should have been 2 assets returned"
        for asset in assets do
            Expect.contains [ darkId; lightId ] asset.Id $"Unexpected asset found ({asset.Id})"
            Expect.equal asset.UpdatedOn Noda.epoch $"Updated on was incorrect ({asset.Id})"
            Expect.isEmpty asset.Data $"Data should not have been retrieved ({asset.Id})"
    }
    
    let ``FindByTheme succeeds when assets do not exist`` (data: IData) = task {
        let! assets = data.ThemeAsset.FindByTheme (ThemeId "no-assets-here")
        Expect.isEmpty assets "There should have been no assets returned"
    }
    
    let ``FindByThemeWithData succeeds when assets exist`` (data: IData) = task {
        let! assets = data.ThemeAsset.FindByThemeWithData assetThemeId
        Expect.hasLength assets 2 "There should have been 2 assets returned"
        let darkLogo = assets |> List.find (fun it -> it.Id = darkId)
        Expect.equal darkLogo.Data darkFile "The dark asset's data is incorrect"
        let lightLogo = assets |> List.find (fun it -> it.Id = lightId)
        Expect.equal lightLogo.Data lightFile "The light asset's data is incorrect"
    }
    
    let ``FindByThemeWithData succeeds when assets do not exist`` (data: IData) = task {
        let! assets = data.ThemeAsset.FindByThemeWithData (ThemeId "still-no-assets")
        Expect.isEmpty assets "There should have been no assets returned"
    }
    
    let ``DeleteByTheme succeeds when assets are deleted`` (data: IData) = task {
        do! data.ThemeAsset.DeleteByTheme assetThemeId
        let! assets = data.ThemeAsset.FindByTheme assetThemeId
        Expect.isEmpty assets "There should be no assets remaining"
    }
    
    let ``DeleteByTheme succeeds when no assets are deleted`` (data: IData) = task {
        do! data.ThemeAsset.DeleteByTheme assetThemeId // already deleted above
        Expect.isTrue true "The above did not raise an exception; that's the test"
    }
