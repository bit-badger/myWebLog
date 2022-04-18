namespace MyWebLog.ViewModels


/// The model to use to allow a user to log on
[<CLIMutable>]
type LogOnModel =
    {   /// The user's e-mail address
        emailAddress : string
    
        /// The user's password
        password : string
    }


/// The model used to display the admin dashboard
type DashboardModel =
    {   /// The number of published posts
        posts : int

        /// The number of post drafts
        drafts : int

        /// The number of pages
        pages : int

        /// The number of pages in the page list
        listedPages : int

        /// The number of categories
        categories : int

        /// The top-level categories
        topLevelCategories : int
    }


/// View model for editing web log settings
[<CLIMutable>]
type SettingsModel =
    {   /// The name of the web log
        name : string

        /// The subtitle of the web log
        subtitle : string

        /// The default page
        defaultPage : string

        /// How many posts should appear on index pages
        postsPerPage : int

        /// The time zone in which dates/times should be displayed
        timeZone : string
    }
