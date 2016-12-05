namespace MyWebLog.Data

open MyWebLog.Entities

/// Interface required to provide data to myWebLog's logic layer
type IMyWebLogData =
  /// Function to set up the data store
  abstract SetUp : (unit -> unit)

  // --- Category ---

  /// Get all categories for a web log
  abstract AllCategories : (string -> Category list)
  
  /// Try to find a category by its Id and web log Id (web log, category Ids)
  abstract CategoryById : (string -> string -> Category option)

  /// Try to find a category by its slug (web log Id, slug)
  abstract CategoryBySlug : (string -> string -> Category option)

  /// Add a category
  abstract AddCategory : (Category -> unit)

  /// Update a category
  abstract UpdateCategory : (Category -> unit)

  /// Update a category's children
  abstract UpdateChildren : (string -> string -> string list -> unit)

  /// Delete a Category
  abstract DeleteCategory : (Category -> unit)

  // --- Page ---

  /// Try to find a page by its Id and web log Id (web log, page Ids), choosing whether to include revisions
  abstract PageById : (string -> string -> bool -> Page option)

  /// Try to find a page by its permalink and web log Id (web log Id, permalink)
  abstract PageByPermalink : (string -> string -> Page option)

  /// Get all pages for a web log
  abstract AllPages : (string -> Page list)

  /// Add a page
  abstract AddPage : (Page -> unit)

  /// Update a page
  abstract UpdatePage : (Page -> unit)

  /// Delete a page by its Id and web log Id (web log, page Ids)
  abstract DeletePage : (string -> string -> unit)

  // --- Post ---

  /// Find a page of published posts for the given web log (web log Id, page #, # per page)
  abstract PageOfPublishedPosts : (string -> int -> int -> Post list)

  /// Find a page of published posts within a given category (web log Id, cat Id, page #, # per page)
  abstract PageOfCategorizedPosts : (string -> string -> int -> int -> Post list)

  /// Find a page of published posts tagged with a given tag (web log Id, tag, page #, # per page)
  abstract PageOfTaggedPosts : (string -> string -> int -> int -> Post list)

  /// Try to find the next newer published post for the given post
  abstract NewerPost : (Post -> Post option)

  /// Try to find the next newer published post within a given category
  abstract NewerCategorizedPost : (string -> Post -> Post option)

  /// Try to find the next newer published post tagged with a given tag
  abstract NewerTaggedPost : (string -> Post -> Post option)

  /// Try to find the next older published post for the given post
  abstract OlderPost : (Post -> Post option)

  /// Try to find the next older published post within a given category
  abstract OlderCategorizedPost : (string -> Post -> Post option)

  /// Try to find the next older published post tagged with a given tag
  abstract OlderTaggedPost : (string -> Post -> Post option)

  /// Find a page of all posts for the given web log (web log Id, page #, # per page)
  abstract PageOfAllPosts : (string -> int -> int -> Post list)

  /// Try to find a post by its Id and web log Id (web log, post Ids)
  abstract PostById : (string -> string -> Post option)

  /// Try to find a post by its permalink (web log Id, permalink)
  abstract PostByPermalink : (string -> string -> Post option)

  /// Try to find a post by a prior permalink (web log Id, permalink)
  abstract PostByPriorPermalink : (string -> string -> Post option)

  /// Get posts for the RSS feed for the given web log and number of posts
  abstract FeedPosts : (string -> int -> (Post * User option) list)

  /// Add a post
  abstract AddPost : (Post -> unit)

  /// Update a post
  abstract UpdatePost : (Post -> unit)

  // --- User ---

  /// Attempt to log on a user
  abstract LogOn : (string -> string -> User option)

  /// Set a user's password (e-mail, password hash)
  abstract SetUserPassword : (string -> string -> unit)

  // --- WebLog ---

  /// Get a web log by its URL base
  abstract WebLogByUrlBase : (string -> WebLog option)

  /// Get dashboard counts for a web log
  abstract DashboardCounts : (string -> DashboardCounts)
