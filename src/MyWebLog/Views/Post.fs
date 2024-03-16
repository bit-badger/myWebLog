module MyWebLog.Views.Post

open Giraffe.Htmx.Common
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Htmx
open MyWebLog
open MyWebLog.ViewModels
open NodaTime.Text

/// The pattern for chapter start times
let startTimePattern = DurationPattern.CreateWithInvariantCulture "H:mm:ss.FF"

/// The form to add or edit a chapter
let chapterEdit (model: EditChapterModel) app = [
    let postUrl = relUrl app $"admin/post/{model.PostId}/chapter/{model.Index}"
    h3 [ _class "my-3" ] [ raw (if model.Index < 0 then "Add" else "Edit"); raw " Chapter" ]
    p [ _class "form-text" ] [
        raw "Times may be entered as seconds; minutes and seconds; or hours, minutes and seconds. Fractional seconds "
        raw "are supported to two decimal places."
    ]
    form [ _method "post"; _action postUrl; _hxPost postUrl; _hxTarget "#chapter_list"; _class "container" ] [
        antiCsrf app
        input [ _type "hidden"; _name "PostId"; _value model.PostId ]
        input [ _type "hidden"; _name "Index"; _value (string model.Index) ]
        div [ _class "row" ] [
            div [ _class "col-6 col-lg-3 mb-3" ] [
                textField [ _required; _autofocus ] (nameof model.StartTime) "Start Time"
                          (if model.Index < 0 then "" else model.StartTime) []
            ]
            div [ _class "col-6 col-lg-3 mb-3" ] [
                textField [] (nameof model.EndTime) "End Time" model.EndTime [
                    span [ _class "form-text" ] [ raw "Optional; ends when next starts" ]
                ]
            ]
            div [ _class "col-12 col-lg-6 mb-3" ] [
                textField [] (nameof model.Title) "Chapter Title" model.Title [
                    span [ _class "form-text" ] [ raw "Optional" ]
                ]
            ]
            div [ _class "col-12 col-lg-6 col-xl-5 mb-3" ] [
                textField [] (nameof model.ImageUrl) "Image URL" model.ImageUrl [
                    span [ _class "form-text" ] [
                        raw "Optional; a separate image to display while this chapter is playing"
                    ]
                ]
            ]
            div [ _class "col-12 col-lg-6 col-xl-5 mb-3" ] [
                textField [] (nameof model.Url) "URL" model.Url [
                    span [ _class "form-text" ] [ raw "Optional; informational link for this chapter" ]
                ]
            ]
            div [ _class "col-12 col-lg-6 offset-lg-3 col-xl-2 offset-xl-0 mb-3 align-self-end d-flex flex-column" ] [
                checkboxSwitch [] (nameof model.IsHidden) "Hidden Chapter" model.IsHidden []
                span [ _class "mt-2 form-text" ] [ raw "Not displayed, but may update image and location" ]
            ]
        ]
        div [ _class "row" ] [
            let hasLoc, attrs = if model.LocationName = "" then false, [ _disabled ] else true, []
            div [ _class "col-12 col-md-4 col-lg-3 offset-lg-1 mb-3 align-self-end" ] [
                checkboxSwitch [ _onclick "Admin.checkChapterLocation()" ] "has_location" "Associate Location" hasLoc []
            ]
            div [ _class "col-12 col-md-8 col-lg-6 offset-lg-1 mb-3" ] [
                textField (_required :: attrs) (nameof model.LocationName) "Name" model.LocationName []
            ]
            div [ _class "col-6 col-lg-4 offset-lg-2 mb-3" ] [
                textField (_required :: attrs) (nameof model.LocationGeo) "Geo URL" model.LocationGeo [
                    em [ _class "form-text" ] [
                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/location/location.md#geo-recommended"
                            _target "_blank"; _relNoOpener ] [
                            raw "see spec"
                        ]
                    ]
                ]
            ]
            div [ _class "col-6 col-lg-4 mb-3" ] [
                textField attrs (nameof model.LocationOsm) "OpenStreetMap ID" model.LocationOsm [
                    em [ _class "form-text" ] [
                        raw "Optional; "
                        a [ _href "https://www.openstreetmap.org/"; _target "_blank"; _relNoOpener ] [ raw "get ID" ]
                        raw ", " 
                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/location/location.md#osm-recommended"
                            _target "_blank"; _relNoOpener ] [
                            raw "see spec"
                        ]
                    ]
                ]
            ]
        ]
        div [ _class "row" ] [
            div [ _class "col" ] [
                let cancelLink = relUrl app $"admin/post/{model.PostId}/chapters"
                if model.Index < 0 then
                    checkboxSwitch [ _checked ] (nameof model.AddAnother) "Add Another New Chapter" true []
                else
                    input [ _type "hidden"; _name "AddAnother"; _value "false" ]
                saveButton; raw " &nbsp; "
                a [ _href cancelLink; _hxGet cancelLink; _class "btn btn-secondary"; _hxTarget "body" ] [ raw "Cancel" ]
            ]
        ]
    ]
]

/// Display a list of chapters
let chapterList withNew (model: ManageChaptersModel) app =
    form [ _method "post"; _id "chapter_list"; _class "container mb-3"; _hxTarget "this"; _hxSwap HxSwap.OuterHtml ] [
        antiCsrf app
        input [ _type "hidden"; _name "Id"; _value model.Id ]
        div [ _class "row mwl-table-heading" ] [
            div [ _class "col-3 col-md-2" ] [ raw "Start" ]
            div [ _class "col-3 col-md-6 col-lg-8" ] [ raw "Title" ]
            div [ _class "col-3 col-md-2 col-lg-1 text-center" ] [ raw "Image?" ]
            div [ _class "col-3 col-md-2 col-lg-1 text-center" ] [ raw "Location?" ]
        ]
        yield! model.Chapters |> List.mapi (fun idx chapter ->
            div [ _class "row mwl-table-detail"; _id $"chapter{idx}" ] [
                div [ _class "col-3 col-md-2" ] [ txt (startTimePattern.Format chapter.StartTime) ]
                div [ _class "col-3 col-md-6 col-lg-8" ] [
                    match chapter.Title with
                    | Some title -> txt title
                    | None -> em [ _class "text-muted" ] [ raw "no title" ]
                    br []
                    small [] [
                        if withNew then
                            raw "&nbsp;"
                        else
                            let chapterUrl = relUrl app $"admin/post/{model.Id}/chapter/{idx}"
                            a [ _href chapterUrl; _hxGet chapterUrl; _hxTarget $"#chapter{idx}"
                                _hxSwap $"{HxSwap.InnerHtml} show:#chapter{idx}:top" ] [
                                raw "Edit"
                            ]
                            span [ _class "text-muted" ] [ raw " &bull; " ]
                            a [ _href chapterUrl; _hxDelete chapterUrl; _class "text-danger" ] [
                                raw "Delete"
                            ]
                    ]
                ]
                div [ _class "col-3 col-md-2 col-lg-1 text-center" ] [ yesOrNo (Option.isSome chapter.ImageUrl) ]
                div [ _class "col-3 col-md-2 col-lg-1 text-center" ] [ yesOrNo (Option.isSome chapter.Location) ]
            ])
        div [ _class "row pb-3"; _id "chapter-1" ] [
            let newLink = relUrl app $"admin/post/{model.Id}/chapter/-1"
            if withNew then
                span [ _hxGet newLink; _hxTarget "#chapter-1"; _hxTrigger "load"; _hxSwap "show:#chapter-1:top" ] []
            else
                div [ _class "row pb-3 mwl-table-detail" ] [
                    div [ _class "col-12" ] [
                        a [ _class "btn btn-primary"; _href newLink; _hxGet newLink; _hxTarget "#chapter-1"
                            _hxSwap "show:#chapter-1:top" ] [
                            raw "Add a New Chapter"
                        ]
                    ]
                ]
        ]
    ]
    |> List.singleton

/// Manage Chapters page
let chapters withNew (model: ManageChaptersModel) app = [
    h2 [ _class "my-3" ] [ txt app.PageTitle ]
    article [] [
        p [ _style "line-height:1.2rem;" ] [
            strong [] [ txt model.Title ]; br []
            small [ _class "text-muted" ] [
                a [ _href (relUrl app $"admin/post/{model.Id}/edit") ] [
                    raw "&laquo; Back to Edit Post"
                ]
            ]
        ]
        yield! chapterList withNew model app
    ]
]

/// Display a list of posts
let list (model: PostDisplay) app = [
    let dateCol   = "col-xs-12 col-md-3 col-lg-2"
    let titleCol  = "col-xs-12 col-md-7 col-lg-6 col-xl-5 col-xxl-4"
    let authorCol = "col-xs-12 col-md-2 col-lg-1"
    let tagCol    = "col-lg-3 col-xl-4 col-xxl-5 d-none d-lg-inline-block"
    h2 [ _class "my-3" ] [ txt app.PageTitle ]
    article [] [
        a [ _href (relUrl app "admin/post/new/edit"); _class "btn btn-primary btn-sm mb-3" ] [ raw "Write a New Post" ]
        if model.Posts.Length > 0 then
            form [ _method "post"; _class "container mb-3"; _hxTarget "body" ] [
                antiCsrf app
                div [ _class "row mwl-table-heading" ] [
                    div [ _class dateCol ] [
                        span [ _class "d-md-none" ] [ raw "Post" ]; span [ _class "d-none d-md-inline" ] [ raw "Date" ]
                    ]
                    div [ _class $"{titleCol} d-none d-md-inline-block" ] [ raw "Title" ]
                    div [ _class $"{authorCol} d-none d-md-inline-block" ] [ raw "Author" ]
                    div [ _class tagCol ] [ raw "Tags" ]
                ]
                for post in model.Posts do
                    div [ _class "row mwl-table-detail" ] [
                        div [ _class $"{dateCol} no-wrap" ] [
                            small [ _class "d-md-none" ] [
                                if post.PublishedOn.HasValue then
                                    raw "Published "; txt (post.PublishedOn.Value.ToString "MMMM d, yyyy")
                                else raw "Not Published"
                                if post.PublishedOn.HasValue && post.PublishedOn.Value <> post.UpdatedOn then
                                    em [ _class "text-muted" ] [
                                        raw " (Updated "; txt (post.UpdatedOn.ToString "MMMM d, yyyy"); raw ")"
                                    ]
                            ]
                            span [ _class "d-none d-md-inline" ] [
                                if post.PublishedOn.HasValue then txt (post.PublishedOn.Value.ToString "MMMM d, yyyy")
                                else raw "Not Published"
                                if not post.PublishedOn.HasValue || post.PublishedOn.Value <> post.UpdatedOn then
                                    br []
                                    small [ _class "text-muted" ] [
                                        em [] [ txt (post.UpdatedOn.ToString "MMMM d, yyyy") ]
                                    ]
                            ]
                        ]
                        div [ _class titleCol ] [
                            if Option.isSome post.Episode then
                                span [ _class "badge bg-success float-end text-uppercase mt-1" ] [ raw "Episode" ]
                            raw post.Title; br []
                            small [] [
                                let postUrl = relUrl app $"admin/post/{post.Id}"
                                a [ _href (relUrl app post.Permalink); _target "_blank" ] [ raw "View Post" ]
                                if app.IsEditor || (app.IsAuthor && app.UserId.Value = WebLogUserId post.AuthorId) then
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href $"{postUrl}/edit" ] [ raw "Edit" ]
                                if app.IsWebLogAdmin then
                                    span [ _class "text-muted" ] [ raw " &bull; " ]
                                    a [ _href postUrl; _hxDelete postUrl; _class "text-danger"
                                        _hxConfirm $"Are you sure you want to delete the post “{post.Title}”? This action cannot be undone." ] [
                                        raw "Delete"
                                    ]
                            ]
                        ]
                        div [ _class authorCol ] [
                            let author =
                                model.Authors
                                |> List.tryFind (fun a -> a.Name = post.AuthorId)
                                |> Option.map _.Value
                                |> Option.defaultValue "--"
                                |> txt
                            small [ _class "d-md-none" ] [
                                raw "Authored by "; author; raw " | "
                                raw (if post.Tags.Length = 0 then "No" else string post.Tags.Length)
                                raw " Tag"; if post.Tags.Length <> 0 then raw "s"
                            ]
                            span [ _class "d-none d-md-inline" ] [ author ]
                        ]
                        div [ _class tagCol ] [
                            let tags =
                                post.Tags |> List.mapi (fun idx tag -> idx, span [ _class "no-wrap" ] [ txt tag ])
                            for tag in tags do
                                snd tag
                                if fst tag < tags.Length - 1 then raw ", "
                        ]
                    ]
            ]
            if Option.isSome model.NewerLink || Option.isSome model.OlderLink then
                div [ _class "d-flex justify-content-evenly mb-3" ] [
                    div [] [
                        if Option.isSome model.NewerLink then
                            p [] [
                                a [ _href model.NewerLink.Value; _class "btn btn-secondary"; ] [
                                    raw "&laquo; Newer Posts"
                                ]
                            ]
                    ]
                    div [ _class "text-right" ] [
                        if Option.isSome model.OlderLink then
                            p [] [
                                a [ _href model.OlderLink.Value; _class "btn btn-secondary" ] [
                                    raw "Older Posts &raquo;"
                                ]
                            ]
                    ]
                ]
        else
            p [ _class "text-muted fst-italic text-center" ] [ raw "This web log has no posts" ]
    ]
]

let postEdit (model: EditPostModel) templates (ratings: MetaItem list) app = [
    h2 [ _class "my-3" ] [ raw app.PageTitle ]
    article [] [
        form [ _action (relUrl app "admin/post/save"); _method "post"; _hxPushUrl "true"; _class "container" ] [
            antiCsrf app
            input [ _type "hidden"; _name (nameof model.Id); _value model.Id ]
            div [ _class "row mb-3" ] [
                div [ _class "col-12 col-lg-9" ] [
                    yield! commonEdit model app
                    textField [ _class "mb-3" ] (nameof model.Tags) "Tags" model.Tags [
                        div [ _class "form-text" ] [ raw "comma-delimited" ]
                    ]
                    if model.Status = string Draft then
                        checkboxSwitch [ _class "mb-2" ] (nameof model.DoPublish) "Publish This Post" model.DoPublish []
                    saveButton
                    hr [ _class "mb-3" ]
                    fieldset [ _class "mb-3" ] [
                        legend [] [
                            span [ _class "form-check form-switch" ] [
                                small [] [
                                    input [ _type "checkbox"; _name (nameof model.IsEpisode)
                                            _id (nameof model.IsEpisode); _class "form-check-input"; _value "true"
                                            _data "bs-toggle" "collapse"; _data "bs-target" "#episode_items"
                                            _onclick "Admin.toggleEpisodeFields()"; if model.IsEpisode then _checked ]
                                ]
                                label [ _for (nameof model.IsEpisode) ] [ raw "Podcast Episode" ]
                            ]
                        ]
                        div [ _id "episode_items"
                              _class $"""container p-0 collapse{if model.IsEpisode then " show" else ""}""" ] [
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-8 pb-3" ] [
                                    textField [ _required ] (nameof model.Media) "Media File" model.Media [
                                        div [ _class "form-text" ] [
                                            raw "Relative URL will be appended to base media path (if set) "
                                            raw "or served from this web log"
                                        ]
                                    ]
                                ]
                                div [ _class "col-12 col-md-4 pb-3" ] [
                                    textField [] (nameof model.MediaType) "Media MIME Type" model.MediaType [
                                        div [ _class "form-text" ] [ raw "Optional; overrides podcast default" ]
                                    ]
                                ]
                            ]
                            div [ _class "row pb-3" ] [
                                div [ _class "col" ] [
                                    numberField [ _required ] (nameof model.Length) "Media Length (bytes)"
                                                (string model.Length) [
                                        div [ _class "form-text" ] [ raw "TODO: derive from above file name" ]
                                    ]
                                ]
                                div [ _class "col" ] [
                                    textField [] (nameof model.Duration) "Duration" model.Duration [
                                        div [ _class "form-text" ] [
                                            raw "Recommended; enter in "; code [] [ raw "HH:MM:SS"]; raw " format"
                                        ]
                                    ]
                                ]
                            ]
                            div [ _class "row pb-3" ] [
                                div [ _class "col" ] [
                                    textField [] (nameof model.Subtitle) "Subtitle" model.Subtitle [
                                        div [ _class "form-text" ] [ raw "Optional; a subtitle for this episode" ]
                                    ]
                                ]
                            ]
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-8 pb-3" ] [
                                    textField [] (nameof model.ImageUrl) "Image URL" model.ImageUrl [
                                        div [ _class "form-text" ] [
                                            raw "Optional; overrides podcast default; "
                                            raw "relative URL served from this web log"
                                        ]
                                    ]
                                ]
                                div [ _class "col-12 col-md-4 pb-3" ] [
                                    selectField [] (nameof model.Explicit) "Explicit Rating" model.Explicit ratings
                                                (_.Name) (_.Value) [
                                        div [ _class "form-text" ] [ raw "Optional; overrides podcast default" ]
                                    ]
                                ]
                            ]
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-8 pb-3" ] [
                                    div [ _class "form-text" ] [ raw "Chapters" ]
                                    div [ _class "form-check form-check-inline" ] [
                                        input [ _type "radio"; _name (nameof model.ChapterSource)
                                                _id "chapter_source_none"; _value "none"; _class "form-check-input"
                                                if model.ChapterSource = "none" then _checked
                                                _onclick "Admin.setChapterSource('none')" ]
                                        label [ _for "chapter_source_none" ] [ raw "None" ]
                                    ]
                                    div [ _class "form-check form-check-inline" ] [
                                        input [ _type "radio"; _name (nameof model.ChapterSource)
                                                _id "chapter_source_internal"; _value "internal"
                                                _class "form-check-input"
                                                if model.ChapterSource= "internal" then _checked
                                                _onclick "Admin.setChapterSource('internal')" ]
                                        label [ _for "chapter_source_internal" ] [ raw "Defined Here" ]
                                    ]
                                    div [ _class "form-check form-check-inline" ] [
                                        input [ _type "radio"; _name (nameof model.ChapterSource)
                                                _id "chapter_source_external"; _value "external"
                                                _class "form-check-input"
                                                if model.ChapterSource = "external" then _checked
                                                _onclick "Admin.setChapterSource('external')" ]
                                        label [ _for "chapter_source_external" ] [ raw "Separate File" ]
                                    ]
                                ]
                                div [ _class "col-md-4 d-flex justify-content-center" ] [
                                    checkboxSwitch [ _class "align-self-center pb-3" ] (nameof model.ContainsWaypoints)
                                                   "Chapters contain waypoints" model.ContainsWaypoints []
                                ]
                            ]
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-8 pb-3" ] [
                                    textField [] (nameof model.ChapterFile) "Chapter File" model.ChapterFile [
                                        div [ _class "form-text" ] [ raw "Relative URL served from this web log" ]
                                    ]
                                ]
                                div [ _class "col-12 col-md-4 pb-3" ] [
                                    textField [] (nameof model.ChapterType) "Chapter MIME Type" model.ChapterType [
                                        div [ _class "form-text" ] [
                                            raw "Optional; "; code [] [ raw "application/json+chapters" ]
                                            raw " assumed if chapter file ends with "; code [] [ raw ".json" ]
                                        ]
                                    ]
                                ]
                            ]
                            div [ _class "row" ] [
                                div [ _class "col-12 col-md-8 pb-3" ] [
                                    textField [ _onkeyup "Admin.requireTranscriptType()" ] (nameof model.TranscriptUrl)
                                              "Transcript URL" model.TranscriptUrl [
                                        div [ _class "form-text" ] [
                                            raw "Optional; relative URL served from this web log"
                                        ]
                                    ]
                                ]
                                div [ _class "col-12 col-md-4 pb-3" ] [
                                    textField [ if model.TranscriptUrl <> "" then _required ]
                                              (nameof model.TranscriptType) "Transcript MIME Type"
                                              model.TranscriptType [
                                        div [ _class "form-text" ] [ raw "Required if transcript URL provided" ]
                                    ]
                                ]
                            ]
                            div [ _class "row pb-3" ] [
                                div [ _class "col" ] [
                                    textField [] (nameof model.TranscriptLang) "Transcript Language"
                                              model.TranscriptLang [
                                        div [ _class "form-text" ] [ raw "Optional; overrides podcast default" ]
                                    ]
                                ]
                                div [ _class "col d-flex justify-content-center" ] [
                                    checkboxSwitch [ _class "align-self-center pb-3" ] (nameof model.TranscriptCaptions)
                                                   "This is a captions file" model.TranscriptCaptions []
                                ]
                            ]
                            div [ _class "row pb-3" ] [
                                div [ _class "col col-md-4" ] [
                                    numberField [] (nameof model.SeasonNumber) "Season Number"
                                                (string model.SeasonNumber) [
                                        div [ _class "form-text" ] [ raw "Optional" ]
                                    ]
                                ]
                                div [ _class "col col-md-8" ] [
                                    textField [ _maxlength "128" ] (nameof model.SeasonDescription) "Season Description"
                                              model.SeasonDescription [
                                        div [ _class "form-text" ] [ raw "Optional" ]
                                    ]
                                ]
                            ]
                            div [ _class "row pb-3" ] [
                                div [ _class "col col-md-4" ] [
                                    numberField [ _step "0.01" ] (nameof model.EpisodeNumber) "Episode Number"
                                                model.EpisodeNumber [
                                        div [ _class "form-text" ] [ raw "Optional; up to 2 decimal points" ]
                                    ]
                                ]
                                div [ _class "col col-md-8" ] [
                                    textField [ _maxlength "128" ] (nameof model.EpisodeDescription)
                                              "Episode Description" model.EpisodeDescription [
                                        div [ _class "form-text" ] [ raw "Optional" ]
                                    ]
                                ]
                            ]
                        ]
                        script [] [
                            raw """document.addEventListener("DOMContentLoaded", () => Admin.toggleEpisodeFields())"""
                        ]
                    ]
                    commonMetaItems model
                    if model.Status = string Published then
                        fieldset [ _class "pb-3" ] [
                            legend [] [ raw "Maintenance" ]
                            div [ _class "container" ] [
                                div [ _class "row" ] [
                                    div [ _class "col align-self-center" ] [
                                        checkboxSwitch [ _class "pb-2" ] (nameof model.SetPublished)
                                                       "Set Published Date" model.SetPublished []
                                    ]
                                    div [ _class "col-4" ] [
                                        div [ _class "form-floating" ] [
                                            input [ _type "datetime-local"; _name (nameof model.PubOverride)
                                                    _id (nameof model.PubOverride); _class "form-control"
                                                    _placeholder "Override Date"
                                                    if model.PubOverride.HasValue then
                                                        _value (model.PubOverride.Value.ToString "yyyy-MM-dd\THH:mm") ]
                                            label [ _for (nameof model.PubOverride); _class "form-label" ] [
                                                raw "Published On"
                                            ]
                                        ]
                                    ]
                                    div [ _class "col-5 align-self-center" ] [
                                        checkboxSwitch [ _class "pb-2" ] (nameof model.SetUpdated)
                                                       "Purge revisions and<br>set as updated date as well"
                                                       model.SetUpdated []
                                    ]
                                ]
                            ]
                        ]
                ]
                div [ _class "col-12 col-lg-3" ] [
                    commonTemplates model templates
                    fieldset [] [
                        legend [] [ raw "Categories" ]
                        for cat in app.Categories do
                            div [ _class "form-check" ] [
                                input [ _type "checkbox"; _name (nameof model.CategoryIds); _id $"category_{cat.Id}"
                                        _class "form-check-input"; _value cat.Id
                                        if model.CategoryIds |> Array.contains cat.Id then _checked ]
                                label [ _for $"category_{cat.Id}"; _class "form-check-label"
                                        match cat.Description with Some it -> _title it | None -> () ] [
                                    yield! cat.ParentNames |> Array.map (fun _ -> raw "&nbsp; &rang; &nbsp;")
                                    txt cat.Name
                                ]
                          ]
                    ]
                ]
            ]
        ]
    ]
    script [] [ raw "window.setTimeout(() => Admin.toggleEpisodeFields(), 500)" ]
]
