module MyWebLog.AdminViews.User

open Giraffe.ViewEngine
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels

/// Page to display the log on page
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
        form [ _method "post"; _class "container g-0"; _hxTarget "this"; _hxSwap "outerHTML show:window:top" ] [
            antiCsrf app
            for user in model do
                div [ _class "row mwl-table-detail"; _id $"user_{user.Id}" ] [
                    div [ _class $"col-12 col-md-4 col-xl-3 no-wrap" ] [
                        txt user.PreferredName; raw " "
                        match user.AccessLevel with
                        | Administrator -> span [ _class $"{badge}-success"   ] [ raw "ADMINISTRATOR" ]
                        | WebLogAdmin   -> span [ _class $"{badge}-primary"   ] [ raw "WEB LOG ADMIN" ]
                        | Editor        -> span [ _class $"{badge}-secondary" ] [ raw "EDITOR" ]
                        | Author        -> span [ _class $"{badge}-dark"      ] [ raw "AUTHOR" ]
                        br []
                        if app.IsAdministrator || (app.IsWebLogAdmin && not (user.AccessLevel = Administrator)) then
                            let urlBase = $"admin/settings/user/{user.Id}"
                            small [] [
                                a [ _href (relUrl app $"{urlBase}/edit"); _hxTarget $"#user_{user.Id}"
                                    _hxSwap $"innerHTML show:#user_{user.Id}:top" ] [
                                    raw "Edit"
                                ]
                                if app.UserId.Value <> user.Id then
                                    let delLink = relUrl app $"{urlBase}/delete"
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href delLink; _hxPost delLink; _class "text-danger"
                                        _hxConfirm $"Are you sure you want to delete the user &ldquo;{user.PreferredName}&rdquo;? This action cannot be undone. (This action will not succeed if the user has authored any posts or pages.)" ] [
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
                    div [ _class "d-none d-xl-block col-xl-2" ] [ longDate user.CreatedOn ]
                    div [ _class "col-12 col-md-4 col-xl-3" ] [
                        match user.LastSeenOn with
                        | Some it -> longDate it; raw " at "; shortTime it
                        | None -> raw "--"
                    ]
                ]
        ]
    ]
    |> List.singleton