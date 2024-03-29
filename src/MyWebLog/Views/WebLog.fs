module MyWebLog.Views.WebLog

open Giraffe.Htmx.Common
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Accessibility
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

/// Form to add or edit a category
let categoryEdit (model: EditCategoryModel) app =
    div [ _class "col-12" ] [
        h5 [ _class "my-3" ] [ raw app.PageTitle ]
        form [ _action (relUrl app "admin/category/save"); _method "post"; _class "container" ] [
            antiCsrf app
            input [ _type "hidden"; _name (nameof model.CategoryId); _value model.CategoryId ]
            div [ _class "row" ] [
                div [ _class "col-12 col-sm-6 col-lg-4 col-xxl-3 offset-xxl-1 mb-3" ] [
                    textField [ _required; _autofocus ] (nameof model.Name) "Name" model.Name []
                ]
                div [ _class "col-12 col-sm-6 col-lg-4 col-xxl-3 mb-3" ] [
                    textField [ _required ] (nameof model.Slug) "Slug" model.Slug []
                ]
                div [ _class "col-12 col-lg-4 col-xxl-3 offset-xxl-1 mb-3" ] [
                    let cats =
                        app.Categories
                        |> Seq.ofArray
                        |> Seq.filter (fun c -> c.Id <> model.CategoryId)
                        |> Seq.map (fun c ->
                            let parents =
                                c.ParentNames
                                |> Array.map (fun it -> $"{it} &rang; ")
                                |> String.concat ""
                            { Name = c.Id; Value = $"{parents}{c.Name}" })
                        |> Seq.append [ { Name = ""; Value = "&ndash; None &ndash;" } ]
                    selectField [] (nameof model.ParentId) "Parent Category" model.ParentId cats (_.Name) (_.Value) []
                ]
                div [ _class "col-12 col-xl-10 offset-xl-1 mb-3" ] [
                    textField [] (nameof model.Description) "Description" model.Description []
                ]
            ]
            div [ _class "row mb-3" ] [
                div [ _class "col text-center" ] [
                    saveButton
                    a [ _href (relUrl app "admin/categories"); _class "btn btn-sm btn-secondary ms-3" ] [ raw "Cancel" ]
                ]
            ]
        ]
    ]
    |> List.singleton


/// Category list page
let categoryList includeNew app = [
    let catCol  = "col-12 col-md-6 col-xl-5 col-xxl-4"
    let descCol = "col-12 col-md-6 col-xl-7 col-xxl-8"
    let categoryDetail (cat: DisplayCategory) =
        div [ _class "row mwl-table-detail"; _id $"cat_{cat.Id}" ] [
            div [ _class $"{catCol} no-wrap" ] [
                if cat.ParentNames.Length > 0 then
                    cat.ParentNames
                    |> Seq.ofArray
                    |> Seq.map (fun it -> raw $"{it} &rang; ")
                    |> List.ofSeq
                    |> small [ _class "text-muted" ]
                raw cat.Name; br []
                small [] [
                    let catUrl = relUrl app $"admin/category/{cat.Id}"
                    if cat.PostCount > 0 then
                        a [ _href (relUrl app $"category/{cat.Slug}"); _target "_blank" ] [
                            raw $"View { cat.PostCount} Post"; if cat.PostCount <> 1 then raw "s"
                        ]; actionSpacer
                    a [ _href $"{catUrl}/edit"; _hxTarget $"#cat_{cat.Id}"
                        _hxSwap $"{HxSwap.InnerHtml} show:#cat_{cat.Id}:top" ] [
                        raw "Edit"
                    ]; actionSpacer
                    a [ _href catUrl; _hxDelete catUrl; _hxTarget "body"; _class "text-danger"
                        _hxConfirm $"Are you sure you want to delete the category “{cat.Name}”? This action cannot be undone." ] [
                        raw "Delete"
                    ]
                ]
            ]
            div [ _class descCol ] [
                match cat.Description with Some value -> raw value | None -> em [ _class "text-muted" ] [ raw "none" ]
            ]
        ]
    let loadNew =
        span [ _hxGet (relUrl app "admin/category/new/edit"); _hxTrigger HxTrigger.Load; _hxSwap HxSwap.OuterHtml ] []

    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        a [ _href (relUrl app "admin/category/new/edit"); _class "btn btn-primary btn-sm mb-3"; _hxTarget "#cat_new" ] [
            raw "Add a New Category"
        ]
        div [ _id "catList"; _class "container" ] [
            if app.Categories.Length = 0 then
                if includeNew then loadNew
                else
                    div [ _id "cat_new" ] [
                        p [ _class "text-muted fst-italic text-center" ] [
                            raw "This web log has no categories defined"
                        ]
                    ]
            else
                div [ _class "container" ] [
                    div [ _class "row mwl-table-heading" ] [
                        div [ _class catCol ] [ raw "Category"; span [ _class "d-md-none" ] [ raw "; Description" ] ]
                        div [ _class $"{descCol} d-none d-md-inline-block" ] [ raw "Description" ]
                    ]
                ]
                form [ _method "post"; _class "container" ] [
                    antiCsrf app
                    div [ _class "row mwl-table-detail"; _id "cat_new" ] [ if includeNew then loadNew ]
                    yield! app.Categories |> Seq.ofArray |> Seq.map categoryDetail |> List.ofSeq
                ]
        ]
    ]
]


/// The main dashboard
let dashboard (model: DashboardModel) app = [
    h2 [ _class "my-3" ] [ txt app.WebLog.Name; raw " &bull; Dashboard" ]
    article [ _class "container" ] [
        div [ _class "row" ] [
            section [ _class "col-lg-5 offset-lg-1 col-xl-4 offset-xl-2 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-primary" ] [ raw "Posts" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3" ] [
                            raw "Published "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Posts) ]
                            raw "&nbsp; Drafts "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Drafts) ]
                        ]
                        if app.IsAuthor then
                            a [ _href (relUrl app "admin/posts"); _class "btn btn-secondary me-2" ] [ raw "View All" ]
                            a [ _href (relUrl app "admin/post/new/edit"); _class "btn btn-primary" ] [
                                raw "Write a New Post"
                            ]
                    ]
                ]
            ]
            section [ _class "col-lg-5 col-xl-4 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-primary" ]  [ raw "Pages" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3" ] [
                            raw "All "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Pages) ]
                            raw "&nbsp; Shown in Page List "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.ListedPages) ]
                        ]
                        if app.IsAuthor then
                            a [ _href (relUrl app "admin/pages"); _class "btn btn-secondary me-2" ] [ raw "View All" ]
                            a [ _href (relUrl app "admin/page/new/edit"); _class "btn btn-primary" ] [
                                raw "Create a New Page"
                            ]
                    ]
                ]
            ]
        ]
        div [ _class "row" ] [
            section [ _class "col-lg-5 offset-lg-1 col-xl-4 offset-xl-2 pb-3" ] [
                div [ _class "card" ] [
                    header [ _class "card-header text-white bg-secondary" ] [ raw "Categories" ]
                    div [ _class "card-body" ] [
                        h6 [ _class "card-subtitle text-muted pb-3"] [
                            raw "All "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.Categories) ]
                            raw "&nbsp; Top Level "
                            span [ _class "badge rounded-pill bg-secondary" ] [ raw (string model.TopLevelCategories) ]
                        ]
                        if app.IsWebLogAdmin then
                            a [ _href (relUrl app "admin/categories"); _class "btn btn-secondary me-2" ] [
                                raw "View All"
                            ]
                            a [ _href (relUrl app "admin/categories?new"); _class "btn btn-secondary" ] [
                                raw "Add a New Category"
                            ]
                    ]
                ]
            ]
        ]
        if app.IsWebLogAdmin then
            div [ _class "row pb-3" ] [
                div [ _class "col text-end" ] [
                    a [ _href (relUrl app "admin/settings"); _class "btn btn-secondary" ] [ raw "Modify Settings" ]
                ]
            ]
    ]
]


/// Custom RSS feed edit form
let feedEdit (model: EditCustomFeedModel) (ratings: MetaItem list) (mediums: MetaItem list) app = [
    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        form [ _action (relUrl app "admin/settings/rss/save"); _method "post"; _class "container" ] [
            antiCsrf app
            input [ _type "hidden"; _name "Id"; _value model.Id ]
            div [ _class "row pb-3" ] [
                div [ _class "col" ] [
                    a [ _href (relUrl app "admin/settings#rss-settings") ] [ raw "&laquo; Back to Settings" ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col-12 col-lg-6" ] [
                    fieldset [ _class "container pb-0" ] [
                        legend [] [ raw "Identification" ]
                        div [ _class "row" ] [
                            div [ _class "col" ] [
                                textField [ _required ] (nameof model.Path) "Relative Feed Path" model.Path [
                                  span [ _class "form-text fst-italic" ] [ raw "Appended to "; txt app.WebLog.UrlBase ]
                                ]
                            ]
                        ]
                        div [ _class "row" ] [
                            div [ _class "col py-3 d-flex align-self-center justify-content-center" ] [
                                checkboxSwitch [ _onclick "Admin.checkPodcast()"; if model.IsPodcast then _checked ]
                                               (nameof model.IsPodcast) "This Is a Podcast Feed" model.IsPodcast []
                            ]
                        ]
                    ]
                ]
                div [ _class "col-12 col-lg-6" ] [
                    fieldset [ _class "container pb-0" ] [
                        legend [] [ raw "Feed Source" ]
                        div [ _class "row d-flex align-items-center" ] [
                            div [ _class "col-1 d-flex justify-content-end pb-3" ] [
                                div [ _class "form-check form-check-inline me-0" ] [
                                    input [ _type "radio"; _name (nameof model.SourceType); _id "SourceTypeCat"
                                            _class "form-check-input"; _value "category"
                                            if model.SourceType <> "tag" then _checked
                                            _onclick "Admin.customFeedBy('category')" ]
                                    label [ _for "SourceTypeCat"; _class "form-check-label d-none" ] [ raw "Category" ]
                                ]
                            ]
                            div [ _class "col-11 pb-3" ] [
                                let cats =
                                    app.Categories
                                    |> Seq.ofArray
                                    |> Seq.map (fun c ->
                                        let parents =
                                            c.ParentNames
                                            |> Array.map (fun it -> $"{it} &rang; ")
                                            |> String.concat ""
                                        { Name = c.Id; Value = $"{parents}{c.Name}" })
                                    |> Seq.append [ { Name = ""; Value = "&ndash; Select Category &ndash;" } ]
                                selectField [ _id "SourceValueCat"; _required
                                              if model.SourceType = "tag" then _disabled ]
                                            (nameof model.SourceValue) "Category" model.SourceValue cats (_.Name)
                                            (_.Value) []
                            ]
                            div [ _class "col-1 d-flex justify-content-end pb-3" ] [
                                div [ _class "form-check form-check-inline me-0" ] [
                                    input [ _type "radio"; _name (nameof model.SourceType); _id "SourceTypeTag"
                                            _class "form-check-input"; _value "tag"
                                            if model.SourceType= "tag" then _checked
                                            _onclick "Admin.customFeedBy('tag')" ]
                                    label [ _for "sourceTypeTag"; _class "form-check-label d-none" ] [ raw "Tag" ]
                                ]
                            ]
                            div [ _class "col-11 pb-3" ] [
                                textField [ _id "SourceValueTag"; _required
                                            if model.SourceType <> "tag" then _disabled ]
                                          (nameof model.SourceValue) "Tag"
                                          (if model.SourceType = "tag" then model.SourceValue else "") []
                            ]
                        ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col" ] [
                    fieldset [ _class "container"; _id "podcastFields"; if not model.IsPodcast then _disabled ] [
                        legend [] [ raw "Podcast Settings" ]
                        div [ _class "row" ] [
                            div [ _class "col-12 col-md-5 col-lg-4 offset-lg-1 pb-3" ] [
                                textField [ _required ] (nameof model.Title) "Title" model.Title []
                            ]
                            div [ _class "col-12 col-md-4 col-lg-4 pb-3" ] [
                                textField [] (nameof model.Subtitle) "Podcast Subtitle" model.Subtitle []
                            ]
                            div [ _class "col-12 col-md-3 col-lg-2 pb-3" ] [
                                numberField [ _required ] (nameof model.ItemsInFeed) "# Episodes"
                                            (string model.ItemsInFeed) []
                            ]
                        ]
                        div [ _class "row" ] [
                            div [ _class "col-12 col-md-5 col-lg-4 offset-lg-1 pb-3" ] [
                                textField [ _required ] (nameof model.AppleCategory) "iTunes Category"
                                          model.AppleCategory [
                                    span [ _class "form-text fst-italic" ] [
                                        a [ _href "https://www.thepodcasthost.com/planning/itunes-podcast-categories/"
                                            _target "_blank"; _relNoOpener ] [
                                            raw "iTunes Category / Subcategory List"
                                        ]
                                    ]
                                ]
                            ]
                            div [ _class "col-12 col-md-4 pb-3" ] [
                                textField [] (nameof model.AppleSubcategory) "iTunes Subcategory" model.AppleSubcategory
                                          []
                            ]
                            div [ _class "col-12 col-md-3 col-lg-2 pb-3" ] [
                                selectField [ _required ] (nameof model.Explicit) "Explicit Rating" model.Explicit
                                            ratings (_.Name) (_.Value) []
                            ]
                        ]
                        div [ _class "row" ] [
                            div [ _class "col-12 col-md-6 col-lg-4 offset-xxl-1 pb-3" ] [
                                textField [ _required ] (nameof model.DisplayedAuthor) "Displayed Author"
                                          model.DisplayedAuthor []
                            ]
                            div [ _class "col-12 col-md-6 col-lg-4 pb-3" ] [
                                emailField [ _required ] (nameof model.Email) "Author E-mail" model.Email [
                                    span [ _class "form-text fst-italic" ] [
                                        raw "For iTunes, must match registered e-mail"
                                    ]
                                ]
                            ]
                            div [ _class "col-12 col-sm-5 col-md-4 col-lg-4 col-xl-3 offset-xl-1 col-xxl-2 offset-xxl-0 pb-3" ] [
                                textField [] (nameof model.DefaultMediaType) "Default Media Type"
                                          model.DefaultMediaType [
                                    span [ _class "form-text fst-italic" ] [ raw "Optional; blank for no default" ]
                                ]
                            ]
                            div [ _class "col-12 col-sm-7 col-md-8 col-lg-10 offset-lg-1 pb-3" ] [
                                textField [ _required ] (nameof model.ImageUrl) "Image URL" model.ImageUrl [
                                    span [ _class "form-text fst-italic"] [
                                        raw "Relative URL will be appended to "; txt app.WebLog.UrlBase; raw "/"
                                    ]
                                ]
                            ]
                        ]
                        div [ _class "row pb-3" ] [
                            div [ _class "col-12 col-lg-10 offset-lg-1" ] [
                                textField [ _required ] (nameof model.Summary) "Summary" model.Summary [
                                    span [ _class "form-text fst-italic" ] [ raw "Displayed in podcast directories" ]
                                ]
                            ]
                        ]
                        div [ _class "row pb-3" ] [
                            div [ _class "col-12 col-lg-10 offset-lg-1" ] [
                                textField [] (nameof model.MediaBaseUrl) "Media Base URL" model.MediaBaseUrl [
                                    span [ _class "form-text fst-italic" ] [
                                        raw "Optional; prepended to episode media file if present"
                                    ]
                                ]
                            ]
                        ]
                        div [ _class "row" ] [
                            div [ _class "col-12 col-lg-5 offset-lg-1 pb-3" ] [
                                textField [] (nameof model.FundingUrl) "Funding URL" model.FundingUrl [
                                    span [ _class "form-text fst-italic" ] [
                                        raw "Optional; URL describing donation options for this podcast, "
                                        raw "relative URL supported"
                                    ]
                                ]
                            ]
                            div [ _class "col-12 col-lg-5 pb-3" ] [
                                textField [ _maxlength "128" ] (nameof model.FundingText) "Funding Text"
                                          model.FundingText [
                                    span [ _class "form-text fst-italic" ] [ raw "Optional; text for the funding link" ]
                                ]
                            ]
                        ]
                        div [ _class "row pb-3" ] [
                            div [ _class "col-8 col-lg-5 offset-lg-1 pb-3" ] [
                                textField [] (nameof model.PodcastGuid) "Podcast GUID" model.PodcastGuid [
                                    span [ _class "form-text fst-italic" ] [
                                        raw "Optional; v5 UUID uniquely identifying this podcast; "
                                        raw "once entered, do not change this value ("
                                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/docs/1.0.md#guid"
                                            _target "_blank"; _relNoOpener ] [
                                            raw "documentation"
                                        ]; raw ")"
                                    ]
                                ]
                            ]
                            div [ _class "col-4 col-lg-3 offset-lg-2 pb-3" ] [
                                selectField [] (nameof model.Medium) "Medium" model.Medium mediums (_.Name) (_.Value) [
                                    span [ _class "form-text fst-italic" ] [
                                        raw "Optional; medium of the podcast content ("
                                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/docs/1.0.md#medium"
                                            _target "_blank"; _relNoOpener ] [
                                            raw "documentation"
                                        ]; raw ")"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [ div [ _class "col text-center" ] [ saveButton ] ]
        ]
    ]
]


/// Redirect Rule edit form
let redirectEdit (model: EditRedirectRuleModel) app = [
    let url = relUrl app $"admin/settings/redirect-rules/{model.RuleId}"
    h3 [] [ raw (if model.RuleId < 0 then "Add" else "Edit"); raw " Redirect Rule" ]
    form [ _action url; _hxPost url; _hxTarget "body"; _method "post"; _class "container" ] [
        antiCsrf app
        input [ _type "hidden"; _name "RuleId"; _value (string model.RuleId) ]
        div [ _class "row" ] [
            div [ _class "col-12 col-lg-5 mb-3" ] [
                textField [ _autofocus; _required ] (nameof model.From) "From" model.From [
                    span [ _class "form-text" ] [ raw "From local URL/pattern" ]
                ]
            ]
            div [ _class "col-12 col-lg-5 mb-3" ] [
                textField [ _required ] (nameof model.To) "To" model.To [
                    span [ _class "form-text" ] [ raw "To URL/pattern" ]
                ]
            ]
            div [ _class "col-12 col-lg-2 mb-3" ] [
                checkboxSwitch [] (nameof model.IsRegex) "Use RegEx" model.IsRegex []
            ]
        ]
        if model.RuleId < 0 then
            div [ _class "row mb-3" ] [
                div [ _class "col-12 text-center" ] [
                    label [ _class "me-1" ] [ raw "Add Rule" ]
                    div [ _class "btn-group btn-group-sm"; _roleGroup; _ariaLabel "New rule placement button group" ] [
                        input [ _type "radio"; _name "InsertAtTop"; _id "at_top"; _class "btn-check"; _value "true" ]
                        label [ _class "btn btn-sm btn-outline-secondary"; _for "at_top" ] [ raw "Top" ]
                        input [ _type "radio"; _name "InsertAtTop"; _id "at_bot"; _class "btn-check"; _value "false"
                                _checked ]
                        label [ _class "btn btn-sm btn-outline-secondary"; _for "at_bot" ] [ raw "Bottom" ]
                    ]
                ]
            ]
        div [ _class "row mb-3" ] [
            div [ _class "col text-center" ] [
                saveButton; raw " &nbsp; "
                a [ _href (relUrl app "admin/settings/redirect-rules"); _class "btn btn-sm btn-secondary ms-3" ] [
                    raw "Cancel"
                ]
            ]
        ]
    ]
]


/// The list of current redirect rules
let redirectList (model: RedirectRule list) app = [
    // Generate the detail for a redirect rule
    let ruleDetail idx (rule: RedirectRule) =
        let ruleId = $"rule_{idx}"
        div [ _class "row mwl-table-detail"; _id ruleId ] [
            div [ _class "col-5 no-wrap" ] [
                txt rule.From; br []
                small [] [
                    let ruleUrl = relUrl app $"admin/settings/redirect-rules/{idx}"
                    a [ _href ruleUrl; _hxTarget $"#{ruleId}"; _hxSwap $"{HxSwap.InnerHtml} show:#{ruleId}:top" ] [
                        raw "Edit"
                    ]
                    if idx > 0 then
                        actionSpacer; a [ _href $"{ruleUrl}/up"; _hxPost $"{ruleUrl}/up" ] [ raw "Move Up" ]
                    if idx <> model.Length - 1 then
                        actionSpacer; a [ _href $"{ruleUrl}/down"; _hxPost $"{ruleUrl}/down" ] [ raw "Move Down" ]
                    actionSpacer
                    a [ _class "text-danger"; _href ruleUrl; _hxDelete ruleUrl
                        _hxConfirm "Are you sure you want to delete this redirect rule?" ] [
                        raw "Delete"
                    ]
                ]
            ]
            div [ _class "col-5" ] [ txt rule.To ]
            div [ _class "col-2 text-center" ] [ yesOrNo rule.IsRegex ]
        ]
    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        p [ _class "mb-3" ] [
            a [ _href (relUrl app "admin/settings") ] [ raw "&laquo; Back to Settings" ]
        ]
        div [ _class "container" ] [
            p [] [
                a [ _href (relUrl app "admin/settings/redirect-rules/-1"); _class "btn btn-primary btn-sm mb-3"
                    _hxTarget "#rule_new" ] [
                    raw "Add Redirect Rule"
                ]
            ]
            if List.isEmpty model then
                div [ _id "rule_new" ] [
                    p [ _class "text-muted text-center fst-italic" ] [
                        raw "This web log has no redirect rules defined"
                    ]
                ]
            else
                div [ _class "container g-0" ] [
                    div [ _class "row mwl-table-heading" ] [
                        div [ _class "col-5" ] [ raw "From" ]
                        div [ _class "col-5" ] [ raw "To" ]
                        div [ _class "col-2 text-center" ] [ raw "RegEx?" ]
                    ]
                ]
                div [ _class "row mwl-table-detail"; _id "rule_new" ] []
                form [ _method "post"; _class "container g-0"; _hxTarget "body" ] [
                    antiCsrf app; yield! List.mapi ruleDetail model
                ]
        ]
        p [ _class "mt-3 text-muted fst-italic text-center" ] [
            raw "This is an advanced feature; please "
            a [ _href "https://bitbadger.solutions/open-source/myweblog/advanced.html#redirect-rules"
                _target "_blank" ] [
                raw "read and understand the documentation on this feature"
            ]
            raw " before adding rules."
        ]
    ]
]


/// Edit a tag mapping
let tagMapEdit (model: EditTagMapModel) app = [
    h5 [ _class "my-3" ] [ txt app.PageTitle ]
    form [ _hxPost (relUrl app "admin/settings/tag-mapping/save"); _method "post"; _class "container"
           _hxTarget "#tagList"; _hxSwap $"{HxSwap.OuterHtml} show:window:top" ] [
        antiCsrf app
        input [ _type "hidden"; _name "Id"; _value model.Id ]
        div [ _class "row mb-3" ] [
            div [ _class "col-6 col-lg-4 offset-lg-2" ] [
                textField [ _autofocus; _required ] (nameof model.Tag) "Tag" model.Tag []
            ]
            div [ _class "col-6 col-lg-4" ] [
                textField [ _required ] (nameof model.UrlValue) "URL Value" model.UrlValue []
            ]
        ]
        div [ _class "row mb-3" ] [
            div [ _class "col text-center" ] [
                saveButton; raw " &nbsp; "
                a [ _href (relUrl app "admin/settings/tag-mappings"); _class "btn btn-sm btn-secondary ms-3" ] [
                    raw "Cancel"
                ]
            ]
        ]
    ]
]


/// Display a list of the web log's current tag mappings
let tagMapList (model: TagMap list) app =
    let tagMapDetail (map: TagMap) =
        let url = relUrl app $"admin/settings/tag-mapping/{map.Id}"
        div [ _class "row mwl-table-detail"; _id $"tag_{map.Id}" ] [
            div [ _class "col no-wrap" ] [
                txt map.Tag; br []
                small [] [
                    a [ _href $"{url}/edit"; _hxTarget $"#tag_{map.Id}"
                        _hxSwap $"{HxSwap.InnerHtml} show:#tag_{map.Id}:top" ] [
                        raw "Edit"
                    ]; actionSpacer
                    a [ _href url; _hxDelete url; _class "text-danger"
                        _hxConfirm $"Are you sure you want to delete the mapping for “{map.Tag}”? This action cannot be undone." ] [
                        raw "Delete"
                    ]
                ]
            ]
            div [ _class "col" ] [ txt map.UrlValue ]
        ]
    div [ _id "tagList"; _class "container" ] [
        if List.isEmpty model then
            div [ _id "tag_new" ] [
                p [ _class "text-muted text-center fst-italic" ] [ raw "This web log has no tag mappings" ]
            ]
        else
            div [ _class "container g-0" ] [
                div [ _class "row mwl-table-heading" ] [
                    div [ _class "col" ] [ raw "Tag" ]
                    div [ _class "col" ] [ raw "URL Value" ]
                ]
            ]
            form [ _method "post"; _class "container g-0"; _hxTarget "#tagList"; _hxSwap HxSwap.OuterHtml ] [
                antiCsrf app
                div [ _class "row mwl-table-detail"; _id "tag_new" ] []
                yield! List.map tagMapDetail model
            ]
    ]
    |> List.singleton


/// The list of uploaded files for a web log
let uploadList (model: DisplayUpload seq) app = [
    let webLogBase = $"upload/{app.WebLog.Slug}/"
    let relativeBase = relUrl app $"upload/{app.WebLog.Slug}/"
    let absoluteBase = app.WebLog.AbsoluteUrl(Permalink webLogBase)
    let uploadDetail (upload: DisplayUpload) =
        div [ _class "row mwl-table-detail" ] [
            div [ _class "col-6" ] [
                let badgeClass = if upload.Source = string Disk then "secondary" else "primary"
                let pathAndName = $"{upload.Path}{upload.Name}"
                span [ _class $"badge bg-{badgeClass} text-uppercase float-end mt-1" ] [ raw upload.Source ]
                raw upload.Name; br []
                small [] [
                    a [ _href $"{relativeBase}{pathAndName}"; _target "_blank" ] [ raw "View File" ]
                    actionSpacer; span [ _class "text-muted" ] [ raw "Copy " ]
                    a [ _href $"{absoluteBase}{pathAndName}"; _hxNoBoost
                        _onclick $"return Admin.copyText('{absoluteBase}{pathAndName}', this)" ] [
                        raw "Absolute"
                    ]
                    span [ _class "text-muted" ] [ raw " | " ]
                    a [ _href $"{relativeBase}{pathAndName}"; _hxNoBoost
                        _onclick $"return Admin.copyText('{relativeBase}{pathAndName}', this)" ] [
                        raw "Relative"
                    ]
                    if app.WebLog.ExtraPath <> "" then
                        span [ _class "text-muted" ] [ raw " | " ]
                        a [ _href $"{webLogBase}{pathAndName}"; _hxNoBoost
                            _onclick $"return Admin.copyText('/{webLogBase}{pathAndName}', this)" ] [
                            raw "For Post"
                        ]
                    span [ _class "text-muted" ] [ raw " Link" ]
                    if app.IsWebLogAdmin then
                        actionSpacer
                        let deleteUrl =
                            if upload.Source = string "Disk" then $"admin/upload/disk/{pathAndName}"
                            else $"admin/upload/{upload.Id}"
                            |> relUrl app
                        a [ _href deleteUrl; _hxDelete deleteUrl; _class "text-danger"
                            _hxConfirm $"Are you sure you want to delete {upload.Name}? This action cannot be undone." ] [
                            raw "Delete"
                        ]
                ]
            ]
            div [ _class "col-3" ] [ raw upload.Path ]
            div [ _class "col-3" ] [
                match upload.UpdatedOn with
                | Some updated -> updated.ToString("yyyy-MM-dd/h:mmtt").ToLowerInvariant()
                | None -> "--"
                |> raw
            ]
        ]

    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        a [ _href (relUrl app "admin/upload/new"); _class "btn btn-primary btn-sm mb-3" ] [ raw "Upload a New File" ]
        form [ _method "post"; _class "container"; _hxTarget "body" ] [
            antiCsrf app
            div [ _class "row" ] [
                div [ _class "col text-center" ] [
                    em [ _class "text-muted" ] [ raw "Uploaded files served from" ]; br []; raw relativeBase
                ]
            ]
            if Seq.isEmpty model then
                div [ _class "row" ] [
                    div [ _class "col text-muted fst-italic text-center" ] [
                        br []; raw "This web log has uploaded files"
                    ]
                ]
            else
                div [ _class "row mwl-table-heading" ] [
                    div [ _class "col-6" ] [ raw "File Name" ]
                    div [ _class "col-3" ] [ raw "Path" ]
                    div [ _class "col-3" ] [ raw "File Date/Time" ]
                ]
                yield! model |> Seq.map uploadDetail
        ]
    ]
]


/// Form to upload a new file
let uploadNew app = [
    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        form [ _action (relUrl app "admin/upload/save"); _method "post"; _class "container"
               _enctype "multipart/form-data"; _hxNoBoost ] [
            antiCsrf app
            div [ _class "row" ] [
                div [ _class "col-12 col-md-6 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "file"; _id "file"; _name "File"; _class "form-control"; _placeholder "File"
                                _required ]
                        label [ _for "file" ] [ raw "File to Upload" ]
                    ]
                ]
                div [ _class "col-12 col-md-6 pb-3 d-flex align-self-center justify-content-around" ] [
                    div [ _class "text-center" ] [
                        raw "Destination"; br []
                        div [ _class "btn-group"; _roleGroup; _ariaLabel "Upload destination button group" ] [
                            input [ _type "radio"; _name "Destination"; _id "destination_db"; _class "btn-check"
                                    _value (string Database); if app.WebLog.Uploads = Database then _checked ]
                            label [ _class "btn btn-outline-primary"; _for "destination_db" ] [ raw (string Database) ]
                            input [ _type "radio"; _name "Destination"; _id "destination_disk"; _class "btn-check"
                                    _value (string Disk); if app.WebLog.Uploads= Disk then _checked ]
                            label [ _class "btn btn-outline-secondary"; _for "destination_disk" ] [ raw "Disk" ]
                        ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col text-center" ] [
                    button [ _type "submit"; _class "btn btn-primary" ] [ raw "Upload File" ]
                ]
            ]
        ]
    ]
]


/// Web log settings page
let webLogSettings
        (model: SettingsModel) (themes: Theme list) (pages: Page list) (uploads: UploadDestination list)
        (rss: EditRssModel) (app: AppViewContext) = [
    let feedDetail (feed: CustomFeed) =
        let source =
            match feed.Source with
            | Category (CategoryId catId) ->
                app.Categories
                |> Array.tryFind (fun cat -> cat.Id = catId)
                |> Option.map _.Name
                |> Option.defaultValue "--INVALID; DELETE THIS FEED--"
                |> sprintf "Category: %s"
            | Tag tag -> $"Tag: {tag}"
        div [ _class "row mwl-table-detail" ] [
            div [ _class "col-12 col-md-6" ] [
                txt source
                if Option.isSome feed.Podcast then
                    raw " &nbsp; "; span [ _class "badge bg-primary" ] [ raw "PODCAST" ]
                br []
                small [] [
                    let feedUrl = relUrl app $"admin/settings/rss/{feed.Id}"
                    a [ _href (relUrl app (string feed.Path)); _target "_blank" ] [ raw "View Feed" ]
                    actionSpacer
                    a [ _href $"{feedUrl}/edit" ] [ raw "Edit" ]; actionSpacer
                    a [ _href feedUrl; _hxDelete feedUrl; _class "text-danger"
                        _hxConfirm $"Are you sure you want to delete the custom RSS feed based on {feed.Source}? This action cannot be undone." ] [
                        raw "Delete"
                    ]
                ]
            ]
            div [ _class "col-12 col-md-6" ] [
                small [ _class "d-md-none" ] [ raw "Served at "; txt (string feed.Path) ]
                span [ _class "d-none d-md-inline" ] [ txt (string feed.Path) ]
            ]
        ]

    h2 [ _class "my-3" ] [ txt app.WebLog.Name; raw " Settings" ]
    article [] [
        p [ _class "text-muted" ] [
            raw "Go to: "; a [ _href "#users" ] [ raw "Users" ]; raw " &bull; "
            a [ _href "#rss-settings" ] [ raw "RSS Settings" ]; raw " &bull; "
            a [ _href "#tag-mappings" ] [ raw "Tag Mappings" ]; raw " &bull; "
            a [ _href (relUrl app "admin/settings/redirect-rules") ] [ raw "Redirect Rules" ]
        ]
        fieldset [ _class "container mb-3" ] [
            legend [] [ raw "Web Log Settings" ]
            form [ _action (relUrl app "admin/settings"); _method "post" ] [
                antiCsrf app
                div [ _class "container g-0" ] [
                    div [ _class "row" ] [
                        div [ _class "col-12 col-md-6 col-xl-4 pb-3" ] [
                            textField [ _required; _autofocus ] (nameof model.Name) "Name" model.Name []
                        ]
                        div [ _class "col-12 col-md-6 col-xl-4 pb-3" ] [
                            textField [ _required ] (nameof model.Slug) "Slug" model.Slug [
                                span [ _class "form-text" ] [
                                    span [ _class "badge rounded-pill bg-warning text-dark" ] [ raw "WARNING" ]
                                    raw " changing this value may break links ("
                                    a [ _href "https://bitbadger.solutions/open-source/myweblog/configuring.html#blog-settings"
                                        _target "_blank" ] [
                                            raw "more"
                                    ]; raw ")"
                                ]
                            ]
                        ]
                        div [ _class "col-12 col-md-6 col-xl-4 pb-3" ] [
                            textField [] (nameof model.Subtitle) "Subtitle" model.Subtitle []
                        ]
                        div [ _class "col-12 col-md-6 col-xl-4 offset-xl-1 pb-3" ] [
                            selectField [ _required ] (nameof model.ThemeId) "Theme" model.ThemeId themes
                                        (fun t -> string t.Id) (fun t -> $"{t.Name} (v{t.Version})") []
                        ]
                        div [ _class "col-12 col-md-6 offset-md-1 col-xl-4 offset-xl-0 pb-3" ] [
                            selectField [ _required ] (nameof model.DefaultPage) "Default Page" model.DefaultPage pages
                                        (fun p -> string p.Id) (_.Title) []
                        ]
                        div [ _class "col-12 col-md-4 col-xl-2 pb-3" ] [
                            numberField [ _required; _min "0"; _max "50" ] (nameof model.PostsPerPage) "Posts per Page"
                                        (string model.PostsPerPage) []
                        ]
                    ]
                    div [ _class "row" ] [
                        div [ _class "col-12 col-md-4 col-xl-3 offset-xl-2 pb-3" ] [
                            textField [ _required ] (nameof model.TimeZone) "Time Zone" model.TimeZone []
                        ]
                        div [ _class "col-12 col-md-4 col-xl-2" ] [
                            checkboxSwitch [] (nameof model.AutoHtmx) "Auto-Load htmx" model.AutoHtmx []
                            span [ _class "form-text fst-italic" ] [
                                a [ _href "https://htmx.org"; _target "_blank"; _relNoOpener ] [ raw "What is this?" ]
                            ]
                        ]
                        div [ _class "col-12 col-md-4 col-xl-3 pb-3" ] [
                            selectField [] (nameof model.Uploads) "Default Upload Destination" model.Uploads uploads
                                        string string []
                        ]
                    ]
                    div [ _class "row pb-3" ] [
                        div [ _class "col text-center" ] [
                            button [ _type "submit"; _class "btn btn-primary" ] [ raw "Save Changes" ]
                        ]
                    ]
                ]
            ]
        ]
        fieldset [ _id "users"; _class "container mb-3 pb-0" ] [
            legend [] [ raw "Users" ]
            span [ _hxGet (relUrl app "admin/settings/users"); _hxTrigger HxTrigger.Load; _hxSwap HxSwap.OuterHtml ] []
        ]
        fieldset [ _id "rss-settings"; _class "container mb-3 pb-0" ] [
            legend [] [ raw "RSS Settings" ]
            form [ _action (relUrl app "admin/settings/rss"); _method "post"; _class "container g-0" ] [
                antiCsrf app
                div [ _class "row pb-3" ] [
                    div [ _class "col col-xl-8 offset-xl-2" ] [
                        fieldset [ _class "d-flex justify-content-evenly flex-row" ] [
                            legend [] [ raw "Feeds Enabled" ]
                            checkboxSwitch [] (nameof rss.IsFeedEnabled) "All Posts" rss.IsFeedEnabled []
                            checkboxSwitch [] (nameof rss.IsCategoryEnabled) "Posts by Category" rss.IsCategoryEnabled
                                           []
                            checkboxSwitch [] (nameof rss.IsTagEnabled) "Posts by Tag" rss.IsTagEnabled []
                        ]
                    ]
                ]
                div [ _class "row" ] [
                    div [ _class "col-12 col-sm-6 col-md-3 col-xl-2 offset-xl-2 pb-3" ] [
                        textField [] (nameof rss.FeedName) "Feed File Name" rss.FeedName [
                            span [ _class "form-text" ] [ raw "Default is "; code [] [ raw "feed.xml" ] ]
                        ]
                    ]
                    div [ _class "col-12 col-sm-6 col-md-4 col-xl-2 pb-3" ] [
                        numberField [ _required; _min "0" ] (nameof rss.ItemsInFeed) "Items in Feed"
                                    (string rss.ItemsInFeed) [
                            span [ _class "form-text" ] [
                                raw "Set to &ldquo;0&rdquo; to use &ldquo;Posts per Page&rdquo; setting ("
                                raw (string app.WebLog.PostsPerPage); raw ")"
                            ]
                        ]
                    ]
                    div [ _class "col-12 col-md-5 col-xl-4 pb-3" ] [
                        textField [] (nameof rss.Copyright) "Copyright String" rss.Copyright [
                            span [ _class "form-text" ] [
                                raw "Can be a "
                                a [ _href "https://creativecommons.org/share-your-work/"; _target "_blank"
                                    _relNoOpener ] [
                                    raw "Creative Commons license string"
                                ]
                            ]
                        ]
                    ]
                ]
                div [ _class "row pb-3" ] [
                    div [ _class "col text-center" ] [
                        button [ _type "submit"; _class "btn btn-primary" ] [ raw "Save Changes" ]
                    ]
                ]
            ]
            fieldset [ _class "container mb-3 pb-0" ] [
                legend [] [ raw "Custom Feeds" ]
                a [ _class "btn btn-sm btn-secondary"; _href (relUrl app "admin/settings/rss/new/edit") ] [
                    raw "Add a New Custom Feed"
                ]
                if app.WebLog.Rss.CustomFeeds.Length = 0 then
                    p [ _class "text-muted fst-italic text-center" ] [ raw "No custom feeds defined" ]
                else
                    form [ _method "post"; _class "container g-0"; _hxTarget "body" ] [
                        antiCsrf app
                        div [ _class "row mwl-table-heading" ] [
                            div [ _class "col-12 col-md-6" ] [
                                span [ _class "d-md-none" ] [ raw "Feed" ]
                                span [ _class "d-none d-md-inline" ] [ raw "Source" ]
                            ]
                            div [ _class "col-12 col-md-6 d-none d-md-inline-block" ] [ raw "Relative Path" ]
                        ]
                        yield! app.WebLog.Rss.CustomFeeds |> List.map feedDetail
                    ]
            ]
        ]
        fieldset [ _id "tag-mappings"; _class "container mb-3 pb-0" ] [
            legend [] [ raw "Tag Mappings" ]
            a [ _href (relUrl app "admin/settings/tag-mapping/new/edit"); _class "btn btn-primary btn-sm mb-3"
                _hxTarget "#tag_new" ] [
                raw "Add a New Tag Mapping"
            ]
            span [ _hxGet (relUrl app "admin/settings/tag-mappings"); _hxTrigger HxTrigger.Load
                   _hxSwap HxSwap.OuterHtml ] []
        ]
    ]
]
