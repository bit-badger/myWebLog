namespace MyWebLog.Features.Posts

open MyWebLog
open MyWebLog.Features.Shared

/// The model used to render multiple posts
type MultiplePostModel (posts : Post seq, webLog) =
    inherit MyWebLogModel (webLog)

    /// The posts to be rendered
    member _.Posts with get () = posts
