namespace MyWebLog.Data

open System
open System.Threading.Tasks
open MyWebLog
open MyWebLog.ViewModels

/// Data functions to support manipulating categories
type ICategoryData =
    
    /// Add a category
    abstract member add : Category -> Task<unit>
    
    /// Count all categories for the given web log
    abstract member countAll : WebLogId -> Task<int>
    
    /// Count all top-level categories for the given web log
    abstract member countTopLevel : WebLogId -> Task<int>
    
    /// Delete a category (also removes it from posts)
    abstract member delete : CategoryId -> WebLogId -> Task<bool>
    
    /// Find all categories for a web log, sorted alphabetically and grouped by hierarchy
    abstract member findAllForView : WebLogId -> Task<DisplayCategory[]>
    
    /// Find a category by its ID
    abstract member findById : CategoryId -> WebLogId -> Task<Category option>
    
    /// Find all categories for the given web log
    abstract member findByWebLog : WebLogId -> Task<Category list>
    
    /// Restore categories from a backup
    abstract member restore : Category list -> Task<unit>
    
    /// Update a category (slug, name, description, and parent ID)
    abstract member update : Category -> Task<unit>


/// Data functions to support manipulating pages
type IPageData =
    
    /// Add a page
    abstract member add : Page -> Task<unit>
    
    /// Get all pages for the web log (excluding meta items, text, revisions, and prior permalinks)
    abstract member all : WebLogId -> Task<Page list>
    
    /// Count all pages for the given web log
    abstract member countAll : WebLogId -> Task<int>
    
    /// Count pages marked as "show in page list" for the given web log
    abstract member countListed : WebLogId -> Task<int>
    
    /// Delete a page
    abstract member delete : PageId -> WebLogId -> Task<bool>
    
    /// Find a page by its ID (excluding revisions and prior permalinks)
    abstract member findById : PageId -> WebLogId -> Task<Page option>
    
    /// Find a page by its permalink (excluding revisions and prior permalinks)
    abstract member findByPermalink : Permalink -> WebLogId -> Task<Page option>
    
    /// Find the current permalink for a page from a list of prior permalinks
    abstract member findCurrentPermalink : Permalink list -> WebLogId -> Task<Permalink option>
    
    /// Find a page by its ID (including revisions and prior permalinks)
    abstract member findFullById : PageId -> WebLogId -> Task<Page option>
    
    /// Find all pages for the given web log (including revisions and prior permalinks)
    abstract member findFullByWebLog : WebLogId -> Task<Page list>
    
    /// Find pages marked as "show in page list" for the given web log (excluding text, revisions, and prior permalinks)
    abstract member findListed : WebLogId -> Task<Page list>
    
    /// Find a page of pages (displayed in admin section) (excluding meta items, revisions and prior permalinks)
    abstract member findPageOfPages : WebLogId -> pageNbr : int -> Task<Page list>
    
    /// Restore pages from a backup
    abstract member restore : Page list -> Task<unit>
    
    /// Update a page
    abstract member update : Page -> Task<unit>
    
    /// Update the prior permalinks for the given page
    abstract member updatePriorPermalinks : PageId -> WebLogId -> Permalink list -> Task<bool>


/// Data functions to support manipulating posts
type IPostData =
    
    /// Add a post
    abstract member add : Post -> Task<unit>
    
    /// Count posts by their status
    abstract member countByStatus : PostStatus -> WebLogId -> Task<int>
    
    /// Delete a post
    abstract member delete : PostId -> WebLogId -> Task<bool>
    
    /// Find a post by its permalink (excluding revisions and prior permalinks)
    abstract member findByPermalink : Permalink -> WebLogId -> Task<Post option>
    
    /// Find the current permalink for a post from a list of prior permalinks
    abstract member findCurrentPermalink : Permalink list -> WebLogId -> Task<Permalink option>
    
    /// Find a post by its ID (including revisions and prior permalinks)
    abstract member findFullById : PostId -> WebLogId -> Task<Post option>
    
    /// Find all posts for the given web log (including revisions and prior permalinks)
    abstract member findFullByWebLog : WebLogId -> Task<Post list>
    
    /// Find posts to be displayed on a category list page (excluding revisions and prior permalinks)
    abstract member findPageOfCategorizedPosts :
        WebLogId -> CategoryId list -> pageNbr : int -> postsPerPage : int -> Task<Post list>
    
    /// Find posts to be displayed on an admin page (excluding revisions and prior permalinks)
    abstract member findPageOfPosts : WebLogId -> pageNbr : int -> postsPerPage : int -> Task<Post list>
    
    /// Find posts to be displayed on a page (excluding revisions and prior permalinks)
    abstract member findPageOfPublishedPosts : WebLogId -> pageNbr : int -> postsPerPage : int -> Task<Post list>
    
    /// Find posts to be displayed on a tag list page (excluding revisions and prior permalinks)
    abstract member findPageOfTaggedPosts :
        WebLogId -> tag : string -> pageNbr : int -> postsPerPage : int -> Task<Post list>
    
    /// Find the next older and newer post for the given published date/time (excluding revisions and prior permalinks)
    abstract member findSurroundingPosts : WebLogId -> publishedOn : DateTime -> Task<Post option * Post option>
    
    /// Restore posts from a backup
    abstract member restore : Post list -> Task<unit>
    
    /// Update a post
    abstract member update : Post -> Task<unit>
    
    /// Update the prior permalinks for a post
    abstract member updatePriorPermalinks : PostId -> WebLogId -> Permalink list -> Task<bool>


/// Functions to manipulate tag mappings
type ITagMapData =
    
    /// Delete a tag mapping
    abstract member delete : TagMapId -> WebLogId -> Task<bool>
    
    /// Find a tag mapping by its ID
    abstract member findById : TagMapId -> WebLogId -> Task<TagMap option>
    
    /// Find a tag mapping by its URL value
    abstract member findByUrlValue : string -> WebLogId -> Task<TagMap option>
    
    /// Retrieve all tag mappings for the given web log
    abstract member findByWebLog : WebLogId -> Task<TagMap list>
    
    /// Find tag mappings for the given tags
    abstract member findMappingForTags : tags : string list -> WebLogId -> Task<TagMap list>
    
    /// Restore tag mappings from a backup
    abstract member restore : TagMap list -> Task<unit>
    
    /// Save a tag mapping (insert or update)
    abstract member save : TagMap -> Task<unit>


/// Functions to manipulate themes
type IThemeData =
    
    /// Retrieve all themes (except "admin")
    abstract member all : unit -> Task<Theme list>
    
    /// Find a theme by its ID
    abstract member findById : ThemeId -> Task<Theme option>
    
    /// Find a theme by its ID (excluding the text of its templates)
    abstract member findByIdWithoutText : ThemeId -> Task<Theme option>
    
    /// Save a theme (insert or update)
    abstract member save : Theme -> Task<unit>


/// Functions to manipulate theme assets
type IThemeAssetData =
    
    /// Retrieve all theme assets (excluding data)
    abstract member all : unit -> Task<ThemeAsset list>
    
    /// Delete all theme assets for the given theme
    abstract member deleteByTheme : ThemeId -> Task<unit>
    
    /// Find a theme asset by its ID
    abstract member findById : ThemeAssetId -> Task<ThemeAsset option>
    
    /// Find all assets for the given theme (excludes data)
    abstract member findByTheme : ThemeId -> Task<ThemeAsset list>
    
    /// Find all assets for the given theme (includes data)
    abstract member findByThemeWithData : ThemeId -> Task<ThemeAsset list>
    
    /// Save a theme asset (insert or update)
    abstract member save : ThemeAsset -> Task<unit>


/// Functions to manipulate uploaded files
type IUploadData =
    
    /// Add an uploaded file
    abstract member add : Upload -> Task<unit>
    
    /// Find an uploaded file by its path for the given web log
    abstract member findByPath : string -> WebLogId -> Task<Upload option>
    
    /// Find all uploaded files for a web log (excludes data)
    abstract member findByWebLog : WebLogId -> Task<Upload list>
    
    /// Find all uploaded files for a web log
    abstract member findByWebLogWithData : WebLogId -> Task<Upload list>
    
    /// Restore uploaded files from a backup
    abstract member restore : Upload list -> Task<unit>


/// Functions to manipulate web logs
type IWebLogData =
    
    /// Add a web log
    abstract member add : WebLog -> Task<unit>
    
    /// Retrieve all web logs
    abstract member all : unit -> Task<WebLog list>
    
    /// Delete a web log, including categories, tag mappings, posts/comments, and pages
    abstract member delete : WebLogId -> Task<unit>
    
    /// Find a web log by its host (URL base)
    abstract member findByHost : string -> Task<WebLog option>
    
    /// Find a web log by its ID
    abstract member findById : WebLogId -> Task<WebLog option>
    
    /// Update RSS options for a web log
    abstract member updateRssOptions : WebLog -> Task<unit>
    
    /// Update web log settings (from the settings page)
    abstract member updateSettings : WebLog -> Task<unit>


/// Functions to manipulate web log users
type IWebLogUserData =
    
    /// Add a web log user
    abstract member add : WebLogUser -> Task<unit>
    
    /// Find a web log user by their e-mail address
    abstract member findByEmail : email : string -> WebLogId -> Task<WebLogUser option>
    
    /// Find a web log user by their ID
    abstract member findById : WebLogUserId -> WebLogId -> Task<WebLogUser option>
    
    /// Find all web log users for the given web log
    abstract member findByWebLog : WebLogId -> Task<WebLogUser list>
    
    /// Get a user ID -> name dictionary for the given user IDs
    abstract member findNames : WebLogId -> WebLogUserId list -> Task<MetaItem list>
    
    /// Restore users from a backup
    abstract member restore : WebLogUser list -> Task<unit>
    
    /// Update a web log user
    abstract member update : WebLogUser -> Task<unit>


/// Data interface required for a myWebLog data implementation
type IData =
    
    /// Category data functions
    abstract member Category : ICategoryData
    
    /// Page data functions
    abstract member Page : IPageData
    
    /// Post data functions
    abstract member Post : IPostData
    
    /// Tag map data functions
    abstract member TagMap : ITagMapData
    
    /// Theme data functions
    abstract member Theme : IThemeData
    
    /// Theme asset data functions
    abstract member ThemeAsset : IThemeAssetData
    
    /// Uploaded file functions
    abstract member Upload : IUploadData
    
    /// Web log data functions
    abstract member WebLog : IWebLogData
    
    /// Web log user data functions
    abstract member WebLogUser : IWebLogUserData
    
    /// Do any required start up data checks
    abstract member startUp : unit -> Task<unit>
    