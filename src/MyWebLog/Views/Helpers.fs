[<AutoOpen>]
module MyWebLog.Views.Helpers

open Microsoft.AspNetCore.Antiforgery
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Accessibility
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels
open NodaTime
open NodaTime.Text

/// The rendering context for this application
[<NoComparison; NoEquality>]
type AppViewContext = {
    /// The web log for this request
    WebLog: WebLog
    
    /// The ID of the current user
    UserId: WebLogUserId option
    
    /// The title of the page being rendered
    PageTitle: string
    
    /// The anti-Cross Site Request Forgery (CSRF) token set to use when rendering a form
    Csrf: AntiforgeryTokenSet option
    
    /// The page list for the web log
    PageList: DisplayPage array
    
    /// Categories and post counts for the web log
    Categories: DisplayCategory array
    
    /// The URL of the page being rendered
    CurrentPage: string
    
    /// User messages
    Messages: UserMessage array
    
    /// The generator string for the rendered page
    Generator: string
    
    /// A string to load the minified htmx script
    HtmxScript: string
    
    /// Whether the current user is an author
    IsAuthor: bool
    
    /// Whether the current user is an editor (implies author)
    IsEditor: bool
    
    /// Whether the current user is a web log administrator (implies author and editor)
    IsWebLogAdmin: bool
    
    /// Whether the current user is an installation administrator (implies all web log rights)
    IsAdministrator: bool
} with
    
    /// Whether there is a user logged on
    member this.IsLoggedOn = Option.isSome this.UserId


/// Create a relative URL for the current web log
let relUrl app =
    Permalink >> app.WebLog.RelativeUrl

/// Add a hidden input with the anti-Cross Site Request Forgery (CSRF) token
let antiCsrf app =
    input [ _type "hidden"; _name app.Csrf.Value.FormFieldName; _value app.Csrf.Value.RequestToken ]

/// Shorthand for encoded text in a template
let txt = encodedText

/// Shorthand for raw text in a template
let raw = rawText

/// The pattern for a long date
let longDatePattern =
    ZonedDateTimePattern.CreateWithInvariantCulture("MMMM d, yyyy", DateTimeZoneProviders.Tzdb)

/// Create a long date
let longDate app (instant: Instant) =
    DateTimeZoneProviders.Tzdb[app.WebLog.TimeZone]
    |> Option.ofObj
    |> Option.map (fun tz -> longDatePattern.Format(instant.InZone(tz)))
    |> Option.defaultValue "--"
    |> txt

/// The pattern for a short time
let shortTimePattern =
    ZonedDateTimePattern.CreateWithInvariantCulture("h:mmtt", DateTimeZoneProviders.Tzdb)

/// Create a short time
let shortTime app (instant: Instant) =
    DateTimeZoneProviders.Tzdb[app.WebLog.TimeZone]
    |> Option.ofObj
    |> Option.map (fun tz -> shortTimePattern.Format(instant.InZone(tz)).ToLowerInvariant())
    |> Option.defaultValue "--"
    |> txt

/// Functions for generating content in varying layouts
module Layout =
    
    /// Generate the title tag for a page
    let private titleTag (app: AppViewContext) =
        title [] [ txt app.PageTitle; raw " &laquo; Admin &laquo; "; txt app.WebLog.Name ]

    /// Create a navigation link
    let private navLink app name url =
        let extraPath = app.WebLog.ExtraPath
        let path = if extraPath = "" then "" else $"{extraPath[1..]}/"
        let active = if app.CurrentPage.StartsWith $"{path}{url}" then " active" else ""
        li [ _class "nav-item" ] [
            a [ _class $"nav-link{active}"; _href (relUrl app url) ] [ txt name ]
        ]

    /// Create a page view for the given content
    let private pageView  (content: AppViewContext -> XmlNode list) app = [
        header [] [
            nav [ _class "navbar navbar-dark bg-dark navbar-expand-md justify-content-start px-2 position-fixed top-0 w-100" ] [
                div [ _class "container-fluid" ] [
                    a [ _class "navbar-brand"; _href (relUrl app ""); _hxNoBoost ] [ txt app.WebLog.Name ]
                    button [ _type "button"; _class "navbar-toggler"; _data "bs-toggle" "collapse"
                             _data "bs-target" "#navbarText"; _ariaControls "navbarText"; _ariaExpanded "false"
                             _ariaLabel "Toggle navigation" ] [
                        span [ _class "navbar-toggler-icon" ] []
                    ]
                    div [ _class "collapse navbar-collapse"; _id "navbarText" ] [
                        if app.IsLoggedOn then
                            ul [ _class "navbar-nav" ] [
                                navLink app "Dashboard" "admin/dashboard"
                                if app.IsAuthor then
                                    navLink app "Pages"   "admin/pages"
                                    navLink app "Posts"   "admin/posts"
                                    navLink app "Uploads" "admin/uploads"
                                if app.IsWebLogAdmin then
                                    navLink app "Categories" "admin/categories"
                                    navLink app "Settings"   "admin/settings"
                                if app.IsAdministrator then navLink app "Admin" "admin/administration"
                            ]
                        ul [ _class "navbar-nav flex-grow-1 justify-content-end" ] [
                            if app.IsLoggedOn then navLink app "My Info" "admin/my-info"
                            li [ _class "nav-item" ] [
                                a [ _class "nav-link"
                                    _href "https://bitbadger.solutions/open-source/myweblog/#how-to-use-myweblog"
                                    _target "_blank" ] [
                                    raw "Docs"
                                ]
                            ]
                            if app.IsLoggedOn then
                                li [ _class "nav-item" ] [
                                    a [ _class "nav-link"; _href (relUrl app "user/log-off"); _hxNoBoost ] [
                                        raw "Log Off"
                                    ]
                                ]
                            else
                                navLink app "Log On" "user/log-on"
                        ]
                    ]
                ]
            ]
        ]
        div [ _id "toastHost"; _class "position-fixed top-0 w-100"; _ariaLive "polite"; _ariaAtomic "true" ] [
            div [ _id "toasts"; _class "toast-container position-absolute p-3 mt-5 top-0 end-0" ] [
                for msg in app.Messages do
                    let textColor = if msg.Level = "warning" then "" else " text-white"
                    div [ _class "toast"; _roleAlert; _ariaLive "assertive"; _ariaAtomic "true"
                          if msg.Level <> "success" then _data "bs-autohide" "false" ] [
                        div [ _class $"toast-header bg-{msg.Level}{textColor}" ] [
                            strong [ _class "me-auto text-uppercase" ] [
                                raw (if msg.Level = "danger" then "error" else msg.Level)
                            ]
                            button [ _type "button"; _class "btn-close"; _data "bs-dismiss" "toast"
                                     _ariaLabel "Close" ] []
                        ]
                        div [ _class $"toast-body bg-{msg.Level} bg-opacity-25" ] [
                            txt msg.Message
                            if Option.isSome msg.Detail then
                                hr []
                                txt msg.Detail.Value
                        ]
                    ]
            ]
        ]
        main [ _class "mx-3 mt-3" ] [
            div [ _class "load-overlay p-5"; _id "loadOverlay" ] [ h1 [ _class "p-3" ] [ raw "Loading&hellip;" ] ]
            yield! content app
        ]
        footer [ _class "position-fixed bottom-0 w-100" ] [
            div [ _class "text-end text-white me-2" ] [
                let version = app.Generator.Split ' '
                small [ _class "me-1 align-baseline"] [ raw $"v{version[1]}" ]
                img [ _src (relUrl app "themes/admin/logo-light.png"); _alt "myWebLog"; _width "120"; _height "34" ]
            ]
        ]
    ]
    
    /// Render a page with a partial layout (htmx request)
    let partial content app =
        html [ _lang "en" ] [
            titleTag app
            yield! pageView content app
        ]
    
    /// Render a page with a full layout
    let full content app =
        html [ _lang "en" ] [
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
            meta [ _name "generator"; _content app.Generator ]
            titleTag app
            link [ _rel "stylesheet"; _href "https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css"
                   _integrity "sha384-1BmE4kWBq78iYhFldvKuhfTAU6auU8tT94WrHftjDbrCEXSU1oBoqyl2QvZ6jIW3"
                   _crossorigin "anonymous" ]
            link [ _rel "stylesheet"; _href (relUrl app "themes/admin/admin.css") ]
            body [ _hxBoost; _hxIndicator "#loadOverlay" ] [
                yield! pageView content app
                script [ _src "https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/js/bootstrap.bundle.min.js"
                         _integrity "sha384-ka7Sk0Gln4gmtz2MlQnikT1wXgYsOg+OMhuP+IlRH9sENBO0LRn5q+8nbTov4+1p"
                         _crossorigin "anonymous" ] []
                Script.minified
                script [ _src (relUrl app "themes/admin/admin.js") ] []
            ]
        ]
    
    /// Render a bare layout
    let bare (content: AppViewContext -> XmlNode list) app =
        html [ _lang "en" ] [
            title [] []
            yield! content app
        ]
