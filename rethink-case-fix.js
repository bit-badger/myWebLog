
// Category
r.db('myWebLog').table('Category').map({
    Description: r.row('description'),
    Id: r.row('id'),
    Name: r.row('name'),
    ParentId: r.row('parentId'),
    Slug: r.row('slug'),
    WebLogId: r.row('webLogId')
})

// Page
r.db('myWebLog').table('Page').map({
    AuthorId: r.row('authorId'),
    Id: r.row('id'),
    Metadata: r.row('metadata').map(function (meta) {
        return { Name: meta('name'), Value: meta('value') }
    }),
    Permalink: r.row('permalink'),
    PriorPermalinks: r.row('priorPermalinks'),
    PublishedOn: r.row('publishedOn'),
    Revisions: r.row('revisions').map(function (rev) {
        return {
            AsOf: rev('asOf'),
            Text: rev('text')
        }
    }),
    IsInPageList: r.row('showInPageList'),
    Template: r.row('template'),
    Text: r.row('text'),
    Title: r.row('title'),
    UpdatedOn: r.row('updatedOn'),
    WebLogId: r.row('webLogId')
})

// Post
r.db('myWebLog').table('Post').map({
    AuthorId: r.row('authorId'),
    CategoryIds: r.row('categoryIds'),
    Episode: r.branch(r.row.hasFields('episode'), {
        Duration: r.row('episode')('duration'),
        Length: r.row('episode')('length'),
        Media: r.row('episode')('media'),
        MediaType: r.row('episode')('mediaType').default(null),
        ImageUrl: r.row('episode')('imageUrl').default(null),
        Subtitle: r.row('episode')('subtitle').default(null),
        Explicit: r.row('episode')('explicit').default(null),
        ChapterFile: r.row('episode')('chapterFile').default(null),
        ChapterType: r.row('episode')('chapterType').default(null),
        TranscriptUrl: r.row('episode')('transcriptUrl').default(null),
        TranscriptType: r.row('episode')('transcriptType').default(null),
        TranscriptLang: r.row('episode')('transcriptLang').default(null),
        TranscriptCaptions: r.row('episode')('transcriptCaptions').default(null),
        SeasonNumber: r.row('episode')('seasonNumber').default(null),
        SeasonDescription: r.row('episode')('seasonDescription').default(null),
        EpisodeNumber: r.row('episode')('episodeNumber').default(null),
        EpisodeDescription: r.row('episode')('episodeDescription').default(null)
    }, null),
    Id: r.row('id'),
    Metadata: r.row('metadata').map(function (meta) {
        return { Name: meta('name'), Value: meta('value') }
    }),
    Permalink: r.row('permalink'),
    PriorPermalinks: r.row('priorPermalinks'),
    PublishedOn: r.row('publishedOn'),
    Revisions: r.row('revisions').map(function (rev) {
        return {
            AsOf: rev('asOf'),
            Text: rev('text')
        }
    }),
    Status: r.row('status'),
    Tags: r.row('tags'),
    Template: r.row('template').default(null),
    Text: r.row('text'),
    Title: r.row('title'),
    UpdatedOn: r.row('updatedOn'),
    WebLogId: r.row('webLogId')
})

// TagMap
r.db('myWebLog').table('TagMap').map({
    Id: r.row('id'),
    Tag: r.row('tag'),
    UrlValue: r.row('urlValue'),
    WebLogId: r.row('webLogId')
})

// Theme
r.db('myWebLog').table('Theme').map({
    Id: r.row('id'),
    Name: r.row('name'),
    Templates: r.row('templates').map(function (tmpl) {
        return {
            Name: tmpl('name'),
            Text: tmpl('text')
        }
    }),
    Version: r.row('version')
})

// ThemeAsset
r.db('myWebLog').table('ThemeAsset').map({
    Data: r.row('data'),
    Id: r.row('id'),
    UpdatedOn: r.row('updatedOn')
})

// WebLog
r.db('myWebLog').table('WebLog').map(
    { AutoHtmx: r.row('autoHtmx'),
        DefaultPage: r.row('defaultPage'),
        Id: r.row('id'),
        Name: r.row('name'),
        PostsPerPage: r.row('postsPerPage'),
        Rss: {
            IsCategoryEnabled: r.row('rss')('categoryEnabled'),
            Copyright: r.row('rss')('copyright'),
            CustomFeeds: r.row('rss')('customFeeds').map(function (feed) {
                return {
                    Id: feed('id'),
                    Path: feed('path'),
                    Podcast: {
                        DefaultMediaType: feed('podcast')('defaultMediaType'),
                        DisplayedAuthor: feed('podcast')('displayedAuthor'),
                        Email: feed('podcast')('email'),
                        Explicit: feed('podcast')('explicit'),
                        FundingText: feed('podcast')('fundingText'),
                        FundingUrl: feed('podcast')('fundingUrl'),
                        PodcastGuid: feed('podcast')('guid'),
                        AppleCategory: feed('podcast')('iTunesCategory'),
                        AppleSubcategory: feed('podcast')('iTunesSubcategory'),
                        ImageUrl: feed('podcast')('imageUrl'),
                        ItemsInFeed: feed('podcast')('itemsInFeed'),
                        MediaBaseUrl: feed('podcast')('mediaBaseUrl'),
                        Medium: feed('podcast')('medium'),
                        Subtitle: feed('podcast')('subtitle'),
                        Summary: feed('podcast')('summary'),
                        Title: feed('podcast')('title')
                    },
                    Source: feed('source')
                }
            }),
            IsFeedEnabled: r.row('rss')('feedEnabled'),
            FeedName: r.row('rss')('feedName'),
            ItemsInFeed: r.row('rss')('itemsInFeed'),
            IsTagEnabled: r.row('rss')('tagEnabled')
        },
        Slug: r.row('slug'),
        Subtitle: r.row('subtitle'),
        ThemeId: r.row('themePath'),
        TimeZone: r.row('timeZone'),
        Uploads: r.row('uploads'),
        UrlBase: r.row('urlBase')
    })

// WebLogUser
r.db('myWebLog').table('WebLogUser').map({
    AccessLevel: r.row('authorizationLevel'),
    FirstName: r.row('firstName'),
    Id: r.row('id'),
    LastName: r.row('lastName'),
    PasswordHash: r.row('passwordHash'),
    PreferredName: r.row('preferredName'),
    Salt: r.row('salt'),
    Url: r.row('url'),
    Email: r.row('userName'),
    WebLogId: r.row('webLogId'),
    CreatedOn: r.branch(r.row.hasFields('createdOn'), r.row('createdOn'), r.expr(new Date(0))),
    LastSeenOn: r.row('lastSeenOn').default(null)
})
