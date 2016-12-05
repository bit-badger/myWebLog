namespace MyWebLog.Data.RethinkDB

open MyWebLog.Data
open RethinkDb.Driver.Net

/// RethinkDB implementation of myWebLog data persistence
type RethinkMyWebLogData(conn : IConnection, cfg : DataConfig) =
  interface IMyWebLogData with
    member __.SetUp = fun () -> SetUp.startUpCheck cfg
    
    member __.AllCategories  = Category.getAllCategories         conn
    member __.CategoryById   = Category.tryFindCategory          conn
    member __.CategoryBySlug = Category.tryFindCategoryBySlug    conn
    member __.AddCategory    = Category.addCategory              conn
    member __.UpdateCategory = Category.updateCategory           conn
    member __.UpdateChildren = Category.updateChildren           conn
    member __.DeleteCategory = Category.deleteCategory           conn

    member __.PageById        = Page.tryFindPageById             conn
    member __.PageByPermalink = Page.tryFindPageByPermalink      conn
    member __.AllPages        = Page.findAllPages                conn
    member __.AddPage         = Page.addPage                     conn
    member __.UpdatePage      = Page.updatePage                  conn
    member __.DeletePage      = Page.deletePage                  conn

    member __.PageOfPublishedPosts   = Post.findPageOfPublishedPosts    conn
    member __.PageOfCategorizedPosts = Post.findPageOfCategorizedPosts  conn
    member __.PageOfTaggedPosts      = Post.findPageOfTaggedPosts       conn
    member __.NewerPost              = Post.tryFindNewerPost            conn
    member __.NewerCategorizedPost   = Post.tryFindNewerCategorizedPost conn
    member __.NewerTaggedPost        = Post.tryFindNewerTaggedPost      conn
    member __.OlderPost              = Post.tryFindOlderPost            conn
    member __.OlderCategorizedPost   = Post.tryFindOlderCategorizedPost conn
    member __.OlderTaggedPost        = Post.tryFindOlderTaggedPost      conn
    member __.PageOfAllPosts         = Post.findPageOfAllPosts          conn
    member __.PostById               = Post.tryFindPost                 conn
    member __.PostByPermalink        = Post.tryFindPostByPermalink      conn
    member __.PostByPriorPermalink   = Post.tryFindPostByPriorPermalink conn
    member __.FeedPosts              = Post.findFeedPosts               conn
    member __.AddPost                = Post.addPost                     conn
    member __.UpdatePost             = Post.updatePost                  conn

    member __.LogOn           = User.tryUserLogOn conn
    member __.SetUserPassword = User.setUserPassword conn

    member __.WebLogByUrlBase = WebLog.tryFindWebLogByUrlBase conn
    member __.DashboardCounts = WebLog.findDashboardCounts    conn
    