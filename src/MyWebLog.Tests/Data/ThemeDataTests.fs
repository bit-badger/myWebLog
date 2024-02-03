/// <summary>
/// Integration tests for <see cref="IThemeData" /> implementations
/// </summary> 
module ThemeDataTests

open Expecto
open MyWebLog
open MyWebLog.Data

/// The ID of the default theme (restored from root-weblog.json)
let private defaultId = ThemeId "default"

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
    let themeId = ThemeId "test-theme"
    do! data.Theme.Save
            { Id        = themeId
              Name      = "Test Theme"
              Version   = "evergreen"
              Templates =
                  [ { Name = "index"; Text = "<h1>{{ values_here }}</h1>" }
                    { Name = "single-post"; Text = "<p>{{ the_post }}" } ] }
    let! saved = data.Theme.FindById themeId
    Expect.isSome saved "There should have been a theme returned"
    let it = saved.Value
    Expect.equal it.Id themeId "ID was incorrect"
    Expect.equal it.Name "Test Theme" "Name was incorrect"
    Expect.equal it.Version "evergreen" "Version was incorrect"
    Expect.hasLength it.Templates 2 "There should have been 2 templates"
    Expect.equal it.Templates[0].Name "index" "Template 0 name incorrect"
    Expect.equal it.Templates[0].Text "<h1>{{ values_here }}</h1>" "Template 0 text incorrect"
    Expect.equal it.Templates[1].Name "single-post" "Template 1 name incorrect"
    Expect.equal it.Templates[1].Text "<p>{{ the_post }}" "Template 1 text incorrect"
}

let ``Save succeeds when updating a theme`` (data: IData) = task {
    let themeId = ThemeId "test-theme"
    do! data.Theme.Save
            { Id        = themeId
              Name      = "Updated Theme"
              Version   = "still evergreen"
              Templates =
                  [ { Name = "index"; Text = "<h1>{{ values_there }}</h1>" }
                    { Name = "layout"; Text = "<!DOCTYPE html><etc />" }
                    { Name = "single-post"; Text = "<p>{{ the_post }}" } ] }
    let! updated = data.Theme.FindById themeId
    Expect.isSome updated "The updated theme should have been returned"
    let it = updated.Value
    Expect.equal it.Id themeId "ID was incorrect"
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
    let! deleted = data.Theme.Delete (ThemeId "test-theme")
    Expect.isTrue deleted "The theme should have been deleted"
}

let ``Delete succeeds when a theme is not deleted`` (data: IData) = task {
    let! deleted = data.Theme.Delete (ThemeId "test-theme") // already deleted above
    Expect.isFalse deleted "The theme should not have been deleted"
}
