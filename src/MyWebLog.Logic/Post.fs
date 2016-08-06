/// Logic for manipulating <see cref="Post" /> entities
module MyWebLog.Logic.Post

open MyWebLog.Data
open MyWebLog.Entities

/// Find a page of published posts
let findPageOfPublishedPosts (data : IMyWebLogData) webLogId pageNbr nbrPerPage =
  data.PageOfPublishedPosts webLogId pageNbr nbrPerPage

/// Find a pages of published posts in a given category
let findPageOfCategorizedPosts (data : IMyWebLogData) webLogId catId pageNbr nbrPerPage =
  data.PageOfCategorizedPosts webLogId catId pageNbr nbrPerPage

/// Find a page of published posts tagged with a given tag
let findPageOfTaggedPosts (data : IMyWebLogData) webLogId tag pageNbr nbrPerPage =
  data.PageOfTaggedPosts webLogId tag pageNbr nbrPerPage

/// Find the next newer published post for the given post
let tryFindNewerPost (data : IMyWebLogData) post = data.NewerPost post

/// Find the next newer published post in a given category for the given post
let tryFindNewerCategorizedPost (data : IMyWebLogData) catId post = data.NewerCategorizedPost catId post

/// Find the next newer published post tagged with a given tag for the given post
let tryFindNewerTaggedPost (data : IMyWebLogData) tag post = data.NewerTaggedPost tag post

/// Find the next older published post for the given post
let tryFindOlderPost (data : IMyWebLogData) post = data.OlderPost post

/// Find the next older published post in a given category for the given post
let tryFindOlderCategorizedPost (data : IMyWebLogData) catId post = data.OlderCategorizedPost catId post

/// Find the next older published post tagged with a given tag for the given post
let tryFindOlderTaggedPost (data : IMyWebLogData) tag post = data.OlderTaggedPost tag post

/// Find a page of all posts for a web log
let findPageOfAllPosts (data : IMyWebLogData) webLogId pageNbr nbrPerPage =
  data.PageOfAllPosts webLogId pageNbr nbrPerPage

/// Try to find a post by its Id
let tryFindPost (data : IMyWebLogData) webLogId postId = data.PostById webLogId postId

/// Try to find a post by its permalink
let tryFindPostByPermalink (data : IMyWebLogData) webLogId permalink = data.PostByPermalink webLogId permalink

/// Try to find a post by its prior permalink
let tryFindPostByPriorPermalink (data : IMyWebLogData) webLogId permalink = data.PostByPriorPermalink webLogId permalink

/// Find posts for the RSS feed
let findFeedPosts (data : IMyWebLogData) webLogId nbrOfPosts = data.FeedPosts webLogId nbrOfPosts

/// Save a post
let savePost (data : IMyWebLogData) post =
  match post.Id with
  | "new" -> let newPost = { post with Id = string <| System.Guid.NewGuid() }
             data.AddPost newPost
             newPost.Id
  | _ -> data.UpdatePost post
         post.Id
