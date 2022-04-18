namespace MyWebLog

open System

/// Support functions for domain definition
[<AutoOpen>]
module private Helpers =

    /// Create a new ID (short GUID)
    // https://www.madskristensen.net/blog/A-shorter-and-URL-friendly-GUID
    let newId() =
        Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('/', '_').Replace('+', '-').Substring (0, 22)


/// An identifier for a category
type CategoryId = CategoryId of string

/// Functions to support category IDs
module CategoryId =
    
    /// An empty category ID
    let empty = CategoryId ""

    /// Convert a category ID to a string
    let toString = function CategoryId ci -> ci
    
    /// Create a new category ID
    let create () = CategoryId (newId ())


/// An identifier for a comment
type CommentId = CommentId of string

/// Functions to support comment IDs
module CommentId =
    
    /// An empty comment ID
    let empty = CommentId ""

    /// Convert a comment ID to a string
    let toString = function CommentId ci -> ci
    
    /// Create a new comment ID
    let create () = CommentId (newId ())


/// Statuses for post comments
type CommentStatus =
    /// The comment is approved
    | Approved
    /// The comment has yet to be approved
    | Pending
    /// The comment was unsolicited and unwelcome
    | Spam


/// The source format for a revision
type RevisionSource =
    /// Markdown text
    | Markdown
    /// HTML
    | Html

/// Functions to support revision sources
module RevisionSource =
    
    /// Convert a revision source to a string representation
    let toString = function Markdown -> "Markdown" | Html -> "HTML"
    
    /// Convert a string to a revision source
    let ofString = function "Markdown" -> Markdown | "HTML" -> Html | x -> invalidArg "string" x

/// A revision of a page or post
[<CLIMutable; NoComparison; NoEquality>]
type Revision =
    {   /// When this revision was saved
        asOf : DateTime

        /// The source language (Markdown or HTML)
        sourceType : RevisionSource

        /// The text of the revision
        text : string
    }

/// Functions to support revisions
module Revision =
    
    /// An empty revision
    let empty =
        { asOf       = DateTime.UtcNow
          sourceType = Html
          text       = ""
        }


/// A permanent link
type Permalink = Permalink of string

/// Functions to support permalinks
module Permalink =
    
    /// An empty permalink
    let empty = Permalink ""

    /// Convert a permalink to a string
    let toString = function Permalink p -> p


/// An identifier for a page
type PageId = PageId of string

/// Functions to support page IDs
module PageId =
    
    /// An empty page ID
    let empty = PageId ""

    /// Convert a page ID to a string
    let toString = function PageId pi -> pi
    
    /// Create a new page ID
    let create () = PageId (newId ())


/// Statuses for posts
type PostStatus =
    /// The post should not be publicly available
    | Draft
    /// The post is publicly viewable
    | Published


/// An identifier for a post
type PostId = PostId of string

/// Functions to support post IDs
module PostId =
    
    /// An empty post ID
    let empty = PostId ""

    /// Convert a post ID to a string
    let toString = function PostId pi -> pi
    
    /// Create a new post ID
    let create () = PostId (newId ())


/// An identifier for a web log
type WebLogId = WebLogId of string

/// Functions to support web log IDs
module WebLogId =
    
    /// An empty web log ID
    let empty = WebLogId ""

    /// Convert a web log ID to a string
    let toString = function WebLogId wli -> wli
    
    /// Create a new web log ID
    let create () = WebLogId (newId ())


/// A level of authorization for a given web log
type AuthorizationLevel =
    /// <summary>The user may administer all aspects of a web log</summary>
    | Administrator
    /// <summary>The user is a known user of a web log</summary>
    | User


/// An identifier for a web log user
type WebLogUserId = WebLogUserId of string

/// Functions to support web log user IDs
module WebLogUserId =
    
    /// An empty web log user ID
    let empty = WebLogUserId ""

    /// Convert a web log user ID to a string
    let toString = function WebLogUserId wli -> wli
    
    /// Create a new web log user ID
    let create () = WebLogUserId (newId ())


