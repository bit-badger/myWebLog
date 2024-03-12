module MyWebLog.Views.User

open Giraffe.Htmx.Common
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

/// User edit form
let edit (model: EditUserModel) app =
    let levelOption value name =
        option [ _value value; if model.AccessLevel = value then _selected ] [ txt name ]
    div [ _class "col-12" ] [
        h5 [ _class "my-3" ] [ txt app.PageTitle ]
        form [ _hxPost (relUrl app "admin/settings/user/save"); _method "post"; _class "container"
               _hxTarget "#userList"; _hxSwap $"{HxSwap.OuterHtml} show:window:top" ] [
            antiCsrf app
            input [ _type "hidden"; _name "Id"; _value model.Id ]
            div [ _class "row" ] [
                div [ _class "col-12 col-md-5 col-lg-3 col-xxl-2 offset-xxl-1 mb-3" ] [
                    div [ _class "form-floating" ] [
                        select [ _name "AccessLevel"; _id "accessLevel"; _class "form-control"; _required
                                 _autofocus ] [
                            levelOption (string Author) "Author"
                            levelOption (string Editor) "Editor"
                            levelOption (string WebLogAdmin) "Web Log Admin"
                            if app.IsAdministrator then levelOption (string Administrator) "Administrator"
                        ]
                        label [ _for "accessLevel" ] [ raw "Access Level" ]
                    ]
                ]
                div [ _class "col-12 col-md-7 col-lg-4 col-xxl-3 mb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "email"; _name "Email"; _id "email"; _class "form-control"; _placeholder "E-mail"
                                _required; _value model.Email ]
                        label [ _for "email" ] [ raw "E-mail Address" ]
                    ]
                ]
                div [ _class "col-12 col-lg-5 mb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "text"; _name "Url"; _id "url"; _class "form-control"; _placeholder "URL"
                                _value model.Url ]
                        label [ _for "url" ] [ raw "User&rsquo;s Personal URL" ]
                    ]
                ]
            ]
            div [ _class "row mb-3" ] [
                div [ _class "col-12 col-md-6 col-lg-4 col-xl-3 offset-xl-1 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "text"; _name "FirstName"; _id "firstName"; _class "form-control"
                                _placeholder "First"; _required; _value model.FirstName ]
                        label [ _for "firstName" ] [ raw "First Name" ]
                    ]
                ]
                div [ _class "col-12 col-md-6 col-lg-4 col-xl-3 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "text"; _name "LastName"; _id "lastName"; _class "form-control"
                                _placeholder "Last"; _required; _value model.LastName ]
                        label [ _for "lastName" ] [ raw "Last Name" ]
                    ]
                ]
                div [ _class "col-12 col-md-6 offset-md-3 col-lg-4 offset-lg-0 col-xl-3 offset-xl-1 pb-3" ] [
                    div [ _class "form-floating " ] [
                        input [ _type "text"; _name "PreferredName"; _id "preferredName"; _class "form-control"
                                _placeholder "Preferred"; _required; _value model.PreferredName ]
                        label [ _for "preferredName" ] [ raw "Preferred Name" ]
                    ]
                ]
            ]
            div [ _class "row mb-3" ] [
                div [ _class "col-12 col-xl-10 offset-xl-1" ] [
                    fieldset [ _class "p-2" ] [
                        legend [ _class "ps-1" ] [
                            if not model.IsNew then raw "Change "
                            raw "Password"
                        ]
                        if not model.IsNew then
                            div [ _class "row" ] [
                                div [ _class "col" ] [
                                    p [ _class "form-text" ] [
                                        raw "Optional; leave blank not change the user&rsquo;s password"
                                    ]
                                ]
                            ]
                        div [ _class "row" ] [
                            div [ _class "col-12 col-md-6 pb-3" ] [
                                div [ _class "form-floating" ] [
                                    input [ _type "password"; _name "Password"; _id "password"; _class "form-control"
                                            _placeholder "Password"
                                            if model.IsNew then _required ]
                                    label [ _for "password" ] [
                                        if not model.IsNew then raw "New "
                                        raw "Password"
                                    ]
                                ]
                            ]
                            div [ _class "col-12 col-md-6 pb-3" ] [
                                div [ _class "form-floating" ] [
                                    input [ _type "password"; _name "PasswordConfirm"; _id "passwordConfirm"
                                            _class "form-control"; _placeholder "Confirm"
                                            if model.IsNew then _required ]
                                    label [ _for "passwordConfirm" ] [
                                        raw "Confirm"
                                        if not model.IsNew then raw " New"
                                        raw " Password"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            div [ _class "row mb-3" ] [
                div [ _class "col text-center" ] [
                    button [ _type "submit"; _class "btn btn-sm btn-primary" ] [ raw "Save Changes" ]; raw " &nbsp; "
                    if model.IsNew then
                        button [ _type "button"; _class "btn btn-sm btn-secondary ms-3"
                                 _onclick "document.getElementById('user_new').innerHTML = ''" ] [
                            raw "Cancel"
                        ]
                    else
                        a [ _href (relUrl app "admin/settings/users"); _class "btn btn-sm btn-secondary ms-3" ] [
                            raw "Cancel"
                        ]
                ]
            ]
        ]
    ]
    |> List.singleton


/// User log on form
let logOn (model: LogOnModel) (app: AppViewContext) = [
    h2 [ _class "my-3" ] [ rawText "Log On to "; encodedText app.WebLog.Name ]
    article [ _class "py-3" ] [
        form [ _action (relUrl app "user/log-on"); _method "post"; _class "container"; _hxPushUrl "true" ] [
            antiCsrf app
            if Option.isSome model.ReturnTo then input [ _type "hidden"; _name "ReturnTo"; _value model.ReturnTo.Value ]
            div [ _class "row" ] [
                div [ _class "col-12 col-md-6 col-lg-4 offset-lg-2 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "email"; _id "email"; _name "EmailAddress"; _class "form-control"; _autofocus
                                _required ]
                        label [ _for "email" ] [ rawText "E-mail Address" ]
                    ]
                ]
                div [ _class "col-12 col-md-6 col-lg-4 pb-3" ] [
                    div [ _class "form-floating" ] [
                        input [ _type "password"; _id "password"; _name "Password"; _class "form-control"; _required ]
                        label [ _for "password" ] [ rawText "Password" ]
                    ]
                ]
            ]
            div [ _class "row pb-3" ] [
                div [ _class "col text-center" ] [
                    button [ _type "submit"; _class "btn btn-primary" ] [ rawText "Log On" ]
                ]
            ]
        ]
    ]
]


/// The list of users for a web log (part of web log settings page)
let userList (model: WebLogUser list) app =
    let badge = "ms-2 badge bg"
    div [ _id "userList" ] [
        div [ _class "container g-0" ] [
            div [ _class "row mwl-table-detail"; _id "user_new" ] []
        ]
        form [ _method "post"; _class "container g-0"; _hxTarget "this"
               _hxSwap $"{HxSwap.OuterHtml} show:window:top" ] [
            antiCsrf app
            for user in model do
                div [ _class "row mwl-table-detail"; _id $"user_{user.Id}" ] [
                    div [ _class "col-12 col-md-4 col-xl-3 no-wrap" ] [
                        txt user.PreferredName; raw " "
                        match user.AccessLevel with
                        | Administrator -> span [ _class $"{badge}-success"   ] [ raw "ADMINISTRATOR" ]
                        | WebLogAdmin   -> span [ _class $"{badge}-primary"   ] [ raw "WEB LOG ADMIN" ]
                        | Editor        -> span [ _class $"{badge}-secondary" ] [ raw "EDITOR" ]
                        | Author        -> span [ _class $"{badge}-dark"      ] [ raw "AUTHOR" ]
                        br []
                        if app.IsAdministrator || (app.IsWebLogAdmin && not (user.AccessLevel = Administrator)) then
                            let userUrl = relUrl app $"admin/settings/user/{user.Id}"
                            small [] [
                                a [ _href $"{userUrl}/edit"; _hxTarget $"#user_{user.Id}"
                                    _hxSwap $"{HxSwap.InnerHtml} show:#user_{user.Id}:top" ] [
                                    raw "Edit"
                                ]
                                if app.UserId.Value <> user.Id then
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href userUrl; _hxDelete userUrl; _class "text-danger"
                                        _hxConfirm $"Are you sure you want to delete the user “{user.PreferredName}”? This action cannot be undone. (This action will not succeed if the user has authored any posts or pages.)" ] [
                                        raw "Delete"
                                    ]
                            ]
                    ]
                    div [ _class "col-12 col-md-4 col-xl-4" ] [
                        txt $"{user.FirstName} {user.LastName}"; br []
                        small [ _class "text-muted" ] [
                            txt user.Email
                            if Option.isSome user.Url then
                                br []; txt user.Url.Value
                        ]
                    ]
                    div [ _class "d-none d-xl-block col-xl-2" ] [
                        if user.CreatedOn = Noda.epoch then raw "N/A" else longDate app user.CreatedOn
                    ]
                    div [ _class "col-12 col-md-4 col-xl-3" ] [
                        match user.LastSeenOn with
                        | Some it -> longDate app it; raw " at "; shortTime app it
                        | None -> raw "--"
                    ]
                ]
        ]
    ]
    |> List.singleton


/// Edit My Info form
let myInfo (model: EditMyInfoModel) (user: WebLogUser) app = [
    h2 [ _class "my-3" ] [ txt app.PageTitle ]
    article [] [
        form [ _action (relUrl app "admin/my-info"); _method "post" ] [
            antiCsrf app
            div [ _class "d-flex flex-row flex-wrap justify-content-around" ] [
                div [ _class "text-center mb-3 lh-sm" ] [
                    strong [ _class "text-decoration-underline" ] [ raw "Access Level" ]; br []
                    raw (string user.AccessLevel)
                ]
                div [ _class "text-center mb-3 lh-sm" ] [
                    strong [ _class "text-decoration-underline" ] [ raw "Created" ]; br []
                    if user.CreatedOn = Noda.epoch then raw "N/A" else longDate app user.CreatedOn
                ]
                div [ _class "text-center mb-3 lh-sm" ] [
                    strong [ _class "text-decoration-underline" ] [ raw "Last Log On" ]; br []
                    longDate app user.LastSeenOn.Value; raw " at "; shortTime app user.LastSeenOn.Value
                ]
            ]
            div [ _class "container" ] [
                div [ _class "row" ] [ div [ _class "col" ] [ hr [ _class "mt-0" ] ] ]
                div [ _class "row mb-3" ] [
                    div [ _class "col-12 col-md-6 col-lg-4 pb-3" ] [
                        div [ _class "form-floating" ] [
                            input [ _type "text"; _name "FirstName"; _id "firstName"; _class "form-control"; _autofocus
                                    _required; _placeholder "First"; _value model.FirstName ]
                            label [ _for "firstName" ] [ raw "First Name" ]
                        ]
                    ]
                    div [ _class "col-12 col-md-6 col-lg-4 pb-3" ] [
                        div [ _class "form-floating" ] [
                            input [ _type "text"; _name "LastName"; _id "lastName"; _class "form-control"; _required
                                    _placeholder "Last"; _value model.LastName ]
                            label [ _for "lastName" ] [ raw "Last Name" ]
                        ]
                    ]
                    div [ _class "col-12 col-md-6 col-lg-4 pb-3" ] [
                        div [ _class "form-floating" ] [
                            input [ _type "text"; _name "PreferredName"; _id "preferredName"; _class "form-control"
                                    _required; _placeholder "Preferred"; _value model.PreferredName ]
                            label [ _for "preferredName" ] [ raw "Preferred Name" ]
                        ]
                    ]
                ]
                div [ _class "row mb-3" ] [
                    div [ _class "col" ] [
                        fieldset [ _class "p-2" ] [
                            legend [ _class "ps-1" ] [ raw "Change Password" ]
                            div [ _class "row" ] [
                                div [ _class "col" ] [
                                    p [ _class "form-text" ] [
                                        raw "Optional; leave blank to keep your current password"
                                    ]
                                ]
                            ]
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-6 pb-3" ] [
                                    div [ _class "form-floating" ] [
                                        input [ _type "password"; _name "NewPassword"; _id "newPassword"
                                                _class "form-control"; _placeholder "Password" ]
                                        label [ _for "newPassword" ] [ raw "New Password" ]
                                    ]
                                ]
                                div [ _class "col-12 col-md-6 pb-3" ] [
                                    div [ _class "form-floating" ] [
                                        input [ _type "password"; _name "NewPasswordConfirm"; _id "newPasswordConfirm"
                                                _class "form-control"; _placeholder "Confirm" ]
                                        label [ _for "newPasswordConfirm" ] [ raw "Confirm New Password" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                div [ _class "row" ] [
                    div [ _class "col text-center mb-3" ] [
                        button [ _type "submit"; _class "btn btn-primary" ] [ raw "Save Changes" ]
                    ]
                ]
            ]
        ]
    ]
]
