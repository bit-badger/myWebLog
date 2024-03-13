module MyWebLog.Views.Admin

open Giraffe.Htmx.Common
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Accessibility
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

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
                            a [ _href (relUrl app "admin/category/new/edit"); _class "btn btn-secondary" ] [
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


/// Redirect Rule edit form
let redirectEdit (model: EditRedirectRuleModel) app = [
    let url = relUrl app $"admin/settings/redirect-rules/{model.RuleId}"
    h3 [] [ raw (if model.RuleId < 0 then "Add" else "Edit"); raw " Redirect Rule" ]
    form [ _action url; _hxPost url; _hxTarget "body"; _method "post"; _class "container" ] [
        antiCsrf app
        input [ _type "hidden"; _name "RuleId"; _value (string model.RuleId) ]
        div [ _class "row" ] [
            div [ _class "col-12 col-lg-5 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _name "From"; _id "from"; _class "form-control"
                            _placeholder "From local URL/pattern"; _autofocus; _required; _value model.From ]
                    label [ _for "from" ] [ raw "From" ]
                ]
            ]
            div [ _class "col-12 col-lg-5 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _name "To"; _id "to"; _class "form-control"; _placeholder "To URL/pattern"
                            _required; _value model.To ]
                    label [ _for "to" ] [ raw "To" ]
                ]
            ]
            div [ _class "col-12 col-lg-2 mb-3" ] [
                div [ _class "form-check form-switch" ] [
                    input [ _type "checkbox"; _name "IsRegex"; _id "isRegex"; _class "form-check-input"; _value "true"
                            if model.IsRegex then _checked ]
                    label [ _for "isRegex" ] [ raw "Use RegEx" ]
                ]
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
                button [ _type "submit"; _class "btn btn-sm btn-primary" ] [ raw "Save Changes" ]
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
                        span [ _class "text-muted" ] [ raw " &bull; " ]
                        a [ _href $"{ruleUrl}/up"; _hxPost $"{ruleUrl}/up" ] [ raw "Move Up" ]
                    if idx <> model.Length - 1 then
                        span [ _class "text-muted" ] [ raw " &bull; " ]
                        a [ _href $"{ruleUrl}/down"; _hxPost $"{ruleUrl}/down" ] [ raw "Move Down" ]
                    span [ _class "text-muted" ] [ raw " &bull; " ]
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
            div [ _class "row" ] [
                div [ _class "col" ] [
                    a [ _href (relUrl app "admin/settings/redirect-rules/-1"); _class "btn btn-primary btn-sm mb-3"
                        _hxTarget "#rule_new" ] [
                        raw "Add Redirect Rule"
                    ]
                ]
            ]
            div [ _class "row" ] [
                div [ _class "col" ] [
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
                div [ _class "form-floating" ] [
                    input [ _type "text"; _name "Tag"; _id "tag"; _class "form-control"; _placeholder "Tag"; _autofocus
                            _required; _value model.Tag ]
                    label [ _for "tag" ] [ raw "Tag" ]
                ]
            ]
            div [ _class "col-6 col-lg-4" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _name "UrlValue"; _id "urlValue"; _class "form-control"
                            _placeholder "URL Value"; _required; _value model.UrlValue ]
                    label [ _for "urlValue" ] [ raw "URL Value" ]
                ]
            ]
        ]
        div [ _class "row mb-3" ] [
            div [ _class "col text-center" ] [
                button [ _type "submit"; _class "btn btn-sm btn-primary" ] [ raw "Save Changes" ]; raw " &nbsp; "
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
                    ]
                    span [ _class "text-muted" ] [ raw " &bull; " ]
                    a [ _href url; _hxDelete url; _class "text-danger"
                        _hxConfirm $"Are you sure you want to delete the mapping for “{map.Tag}”? This action cannot be undone." ] [
                        raw "Delete"
                    ]
                ]
            ]
            div [ _class "col" ] [ txt map.UrlValue ]
        ]
    div [ _id "tagList"; _class "container" ] [
        div [ _class "row" ] [
            div [ _class "col" ] [                   
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
        ]
    ]
    |> List.singleton


/// Display a list of themes
let themeList (model: DisplayTheme list) app =
    let themeCol = "col-12 col-md-6"
    let slugCol  = "d-none d-md-block col-md-3"
    let tmplCol  = "d-none d-md-block col-md-3"
    div [ _id "theme_panel" ] [
        a [ _href (relUrl app "admin/theme/new"); _class "btn btn-primary btn-sm mb-3"; _hxTarget "#theme_new" ] [
            raw "Upload a New Theme"
        ]
        div [ _class "container g-0" ] [
            div [ _class "row mwl-table-heading" ] [
                div [ _class themeCol ] [ raw "Theme" ]
                div [ _class slugCol ] [ raw "Slug" ]
                div [ _class tmplCol ] [ raw "Templates" ]
            ]
        ]
        div [ _class "row mwl-table-detail"; _id "theme_new" ] []
        form [ _method "post"; _id "themeList"; _class "container g-0"; _hxTarget "#theme_panel"
               _hxSwap $"{HxSwap.OuterHtml} show:window:top" ] [
            antiCsrf app
            for theme in model do
                let url = relUrl app $"admin/theme/{theme.Id}"
                div [ _class "row mwl-table-detail"; _id $"theme_{theme.Id}" ] [
                    div [ _class $"{themeCol} no-wrap" ] [
                        txt theme.Name
                        if theme.IsInUse then span [ _class "badge bg-primary ms-2" ] [ raw "IN USE" ]
                        if not theme.IsOnDisk then
                            span [ _class "badge bg-warning text-dark ms-2" ] [ raw "NOT ON DISK" ]
                        br []
                        small [] [
                            span [ _class "text-muted" ] [ txt $"v{theme.Version}" ]
                            if not (theme.IsInUse || theme.Id = "default") then
                                span [ _class "text-muted" ] [ raw " &bull; " ]
                                a [ _href url; _hxDelete url; _class "text-danger"
                                    _hxConfirm $"Are you sure you want to delete the theme “{theme.Name}”? This action cannot be undone." ] [
                                    raw "Delete"
                                ]
                            span [ _class "d-md-none text-muted" ] [
                                br []; raw "Slug: "; txt theme.Id; raw $" &bull; {theme.TemplateCount} Templates"
                            ]
                        ]
                    ]
                    div [ _class slugCol ] [ txt (string theme.Id) ]
                    div [ _class tmplCol ] [ txt (string theme.TemplateCount) ]
                ]
        ]
    ]
    |> List.singleton


/// Form to allow a theme to be uploaded
let themeUpload app =
    div [ _class "col" ] [
        h5 [ _class "mt-2" ] [ raw app.PageTitle ]
        form [ _action (relUrl app "admin/theme/new"); _method "post"; _class "container"
               _enctype "multipart/form-data"; _hxNoBoost ] [
            antiCsrf app
            div [ _class "row " ] [
                div [ _class "col-12 col-sm-6 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "file"; _id "file"; _name "file"; _class "form-control"; _accept ".zip"
                                _placeholder "Theme File"; _required ]
                        label [ _for "file" ] [ raw "Theme File" ]
                    ]
                ]
                div [ _class "col-12 col-sm-6 pb-3 d-flex justify-content-center align-items-center" ] [
                    div [ _class "form-check form-switch pb-2" ] [
                        input [ _type "checkbox"; _name "DoOverwrite"; _id "doOverwrite"; _class "form-check-input"
                                _value "true" ]
                        label [ _for "doOverwrite"; _class "form-check-label" ] [ raw "Overwrite" ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col text-center" ] [
                    button [ _type "submit"; _class "btn btn-sm btn-primary" ] [ raw "Upload Theme" ]; raw " &nbsp; "
                    button [ _type "button"; _class "btn btn-sm btn-secondary ms-3"
                             _onclick "document.getElementById('theme_new').innerHTML = ''" ] [
                        raw "Cancel"
                    ]
                ]
            ]
        ]
    ]
    |> List.singleton
    