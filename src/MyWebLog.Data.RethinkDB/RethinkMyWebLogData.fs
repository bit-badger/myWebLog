namespace MyWebLog.Data.RethinkDB

open MyWebLog.Data
open RethinkDb.Driver.Net

/// RethinkDB implementation of myWebLog data persistence
type RethinkMyWebLogData(conn : IConnection, cfg : DataConfig) =
  interface IMyWebLogData with
    member this.SetUp = fun () -> SetUp.startUpCheck cfg
    
    member this.AllCategories  = Category.getAllCategories         conn
    member this.CategoryById   = Category.tryFindCategory          conn
    member this.CategoryBySlug = Category.tryFindCategoryBySlug    conn
    member this.AddCategory    = Category.addCategory              conn
    member this.UpdateCategory = Category.updateCategory           conn
    member this.UpdateChildren = Category.updateChildren           conn
    member this.DeleteCategory = Category.deleteCategory           conn

    member this.PageById        = Page.tryFindPageById             conn
    member this.PageByPermalink = Page.tryFindPageByPermalink      conn
    member this.AllPages        = Page.findAllPages                conn
    member this.AddPage         = Page.addPage                     conn
    member this.UpdatePage      = Page.updatePage                  conn
    member this.DeletePage      = Page.deletePage                  conn

    member this.PageOfPublishedPosts   = Post.findPageOfPublishedPosts    conn
    member this.PageOfCategorizedPosts = Post.findPageOfCategorizedPosts  conn
    member this.PageOfTaggedPosts      = Post.findPageOfTaggedPosts       conn
    member this.NewerPost              = Post.tryFindNewerPost            conn
    member this.NewerCategorizedPost   = Post.tryFindNewerCategorizedPost conn
    member this.NewerTaggedPost        = Post.tryFindNewerTaggedPost      conn
    member this.OlderPost              = Post.tryFindOlderPost            conn
    member this.OlderCategorizedPost   = Post.tryFindOlderCategorizedPost conn
    member this.OlderTaggedPost        = Post.tryFindOlderTaggedPost      conn
    member this.PageOfAllPosts         = Post.findPageOfAllPosts          conn
    member this.PostById               = Post.tryFindPost                 conn
    member this.PostByPermalink        = Post.tryFindPostByPermalink      conn
    member this.PostByPriorPermalink   = Post.tryFindPostByPriorPermalink conn
    member this.FeedPosts              = Post.findFeedPosts               conn
    member this.AddPost                = Post.addPost                     conn
    member this.UpdatePost             = Post.updatePost                  conn

    member this.LogOn = User.tryUserLogOn conn

    member this.WebLogByUrlBase = WebLog.tryFindWebLogByUrlBase conn
    member this.DashboardCounts = WebLog.findDashboardCounts    conn
    