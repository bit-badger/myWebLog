module MyWebLog.Views.Post

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
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "start_time"; _name "StartTime"; _class "form-control"; _required
                            _autofocus; _placeholder "Start Time"
                            if model.Index >= 0 then _value model.StartTime ]
                    label [ _for "start_time" ] [ raw "Start Time" ]
                ]
            ]
            div [ _class "col-6 col-lg-3 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "end_time"; _name "EndTime"; _class "form-control"; _value model.EndTime
                            _placeholder "End Time" ]
                    label [ _for "end_time" ] [ raw "End Time" ]
                    span [ _class "form-text" ] [ raw "Optional; ends when next starts" ]
                ]
            ]
            div [ _class "col-12 col-lg-6 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "title"; _name "Title"; _class "form-control"; _value model.Title
                            _placeholder "Title" ]
                    label [ _for "title" ] [ raw "Chapter Title" ]
                    span [ _class "form-text" ] [ raw "Optional" ]
                ]
            ]
            div [ _class "col-12 col-lg-6 offset-xl-1 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "image_url"; _name "ImageUrl"; _class "form-control"
                            _value model.ImageUrl; _placeholder "Image URL" ]
                    label [ _for "image_url" ] [ raw "Image URL" ]
                    span [ _class "form-text" ] [
                        raw "Optional; a separate image to display while this chapter is playing"
                    ]
                ]
            ]
            div [ _class "col-12 col-lg-6 col-xl-4 mb-3 align-self-end d-flex flex-column" ] [
                div [ _class "form-check form-switch mb-3" ] [
                    input [ _type "checkbox"; _id "is_hidden"; _name "IsHidden"; _class "form-check-input"
                            _value "true"
                            if model.IsHidden then _checked ]
                    label [ _for "is_hidden" ] [ raw "Hidden Chapter" ]
                ]
                span [ _class "form-text" ] [ raw "Not displayed, but may update image and location" ]
            ]
        ]
        div [ _class "row" ] [
            let hasLoc = model.LocationName <> ""
            div [ _class "col-12 col-md-4 col-lg-3 offset-lg-1 mb-3 align-self-end" ] [
                div [ _class "form-check form-switch mb-3" ] [
                    input [ _type "checkbox"; _id "has_location"; _class "form-check-input"; _value "true"
                            if hasLoc then _checked
                            _onclick "Admin.checkChapterLocation()" ]
                    label [ _for "has_location" ] [ raw "Associate Location" ]
                ]
            ]
            div [ _class "col-12 col-md-8 col-lg-6 offset-lg-1 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "location_name"; _name "LocationName"; _class "form-control"
                            _value model.LocationName; _placeholder "Location Name"; _required
                            if not hasLoc then _disabled ]
                    label [ _for "location_name" ] [ raw "Name" ]
                ]
            ]
            div [ _class "col-6 col-lg-4 offset-lg-2 mb-3" ] [
                div [ _class "form-floating" ] [
                    input [ _type "text"; _id "location_geo"; _name "LocationGeo"; _class "form-control"
                            _value model.LocationGeo; _placeholder "Location Geo URL"
                            if not hasLoc then _disabled ]
                    label [ _for "location_geo" ] [ raw "Geo URL" ]
                    em [ _class "form-text" ] [
                        raw "Optional; "
                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/location/location.md#geo-recommended"
                            _target "_blank"; _rel "noopener" ] [
                            raw "see spec"
                        ]
                    ]
                ]
            ]
            div [ _class "col-6 col-lg-4 mb-3" ] [
                div [ _class "form-floating" ] [ 
                    input [ _type "text"; _id "location_osm"; _name "LocationOsm"; _class "form-control"
                            _value model.LocationOsm; _placeholder "Location OSM Query"
                            if not hasLoc then _disabled ]
                    label [ _for "location_osm" ] [ raw "OpenStreetMap ID" ]
                    em [ _class "form-text" ] [
                        raw "Optional; "
                        a [ _href "https://www.openstreetmap.org/"; _target "_blank"; _rel "noopener" ] [ raw "get ID" ]
                        raw ", " 
                        a [ _href "https://github.com/Podcastindex-org/podcast-namespace/blob/main/location/location.md#osm-recommended"
                            _target "_blank"; _rel "noopener" ] [
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
                    div [ _class "form-check form-switch mb-3" ] [
                        input [ _type "checkbox"; _id "add_another"; _name "AddAnother"; _class "form-check-input"
                                _value "true"; _checked ]
                        label [ _for "add_another" ] [ raw "Add Another New Chapter" ]
                    ]
                else
                    input [ _type "hidden"; _name "AddAnother"; _value "false" ]
                button [ _type "submit"; _class "btn btn-primary" ] [ raw "Save" ]
                raw " &nbsp; "
                a [ _href cancelLink; _hxGet cancelLink; _class "btn btn-secondary"; _hxTarget "body" ] [ raw "Cancel" ]
            ]
        ]
    ]
]

/// Display a list of chapters
let chapterList withNew (model: ManageChaptersModel) app =
    form [ _method "post"; _id "chapter_list"; _class "container mb-3"; _hxTarget "this"; _hxSwap "outerHTML" ] [
        antiCsrf app
        input [ _type "hidden"; _name "Id"; _value model.Id ]
        div [ _class "row mwl-table-heading" ] [
            div [ _class "col" ] [ raw "Start" ]
            div [ _class "col" ] [ raw "Title" ]
            div [ _class "col" ] [ raw "Image?" ]
            div [ _class "col" ] [ raw "Location?" ]
        ]
        yield! model.Chapters |> List.mapi (fun idx chapter ->
            div [ _class "row mwl-table-detail"; _id $"chapter{idx}" ] [
                div [ _class "col" ] [ txt (startTimePattern.Format chapter.StartTime) ]
                div [ _class "col" ] [
                    txt (defaultArg chapter.Title ""); br []
                    small [] [
                        if withNew then
                            raw "&nbsp;"
                        else
                            let chapterUrl = relUrl app $"admin/post/{model.Id}/chapter/{idx}"
                            a [ _href chapterUrl; _hxGet chapterUrl; _hxTarget $"#chapter{idx}"
                                _hxSwap $"innerHTML show:#chapter{idx}:top" ] [
                                raw "Edit"
                            ]
                            span [ _class "text-muted" ] [ raw " &bull; " ]
                            a [ _href chapterUrl; _hxDelete chapterUrl; _class "text-danger" ] [
                                raw "Delete"
                            ]
                    ]
                ]
                div [ _class "col" ] [ raw (if Option.isSome chapter.ImageUrl then "Y" else "N") ]
                div [ _class "col" ] [ raw (if Option.isSome chapter.Location then "Y" else "N") ]
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
