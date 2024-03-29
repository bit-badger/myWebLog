namespace MyWebLog.Data.Postgres

open System.Threading
open System.Threading.Tasks
open BitBadger.Documents.Postgres
open Microsoft.Extensions.Caching.Distributed
open NodaTime

/// Helper types and functions for the cache
[<AutoOpen>]
module private Helpers =
    
    /// The cache entry
    type Entry = {
        /// The ID of the cache entry
        Id: string
        
        /// The value to be cached
        Payload: byte array
        
        /// When this entry will expire
        ExpireAt: Instant
        
        /// The duration by which the expiration should be pushed out when being refreshed
        SlidingExpiration: Duration option
        
        /// The must-expire-by date/time for the cache entry
        AbsoluteExpiration: Instant option
    }
    
    /// Run a task synchronously
    let sync<'T> (it: Task<'T>) = it |> (Async.AwaitTask >> Async.RunSynchronously)
    
    /// Get the current instant
    let getNow () = SystemClock.Instance.GetCurrentInstant()
    
    /// Create a parameter for the expire-at time
    let expireParam =
        typedParam "expireAt"


/// A distributed cache implementation in PostgreSQL used to handle sessions for myWebLog
type DistributedCache () =
    
    // ~~~ INITIALIZATION ~~~
    
    do
        task {
            let! exists =
                Custom.scalar
                    "SELECT EXISTS
                       (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = 'session')
                     AS it"
                    []
                    toExists
            if not exists then
                do! Custom.nonQuery
                        "CREATE TABLE session (
                            id                  TEXT        NOT NULL PRIMARY KEY,
                            payload             BYTEA       NOT NULL,
                            expire_at           TIMESTAMPTZ NOT NULL,
                            sliding_expiration  INTERVAL,
                            absolute_expiration TIMESTAMPTZ);
                        CREATE INDEX idx_session_expiration ON session (expire_at)" []
        } |> sync
    
    // ~~~ SUPPORT FUNCTIONS ~~~
    
    /// Get an entry, updating it for sliding expiration
    let getEntry key = backgroundTask {
        let idParam = "@id", Sql.string key
        let! tryEntry =
            Custom.single
                "SELECT * FROM session WHERE id = @id"
                [ idParam ]
                (fun row ->
                      { Id                 = row.string                     "id"
                        Payload            = row.bytea                      "payload"
                        ExpireAt           = row.fieldValue<Instant>        "expire_at"
                        SlidingExpiration  = row.fieldValueOrNone<Duration> "sliding_expiration"
                        AbsoluteExpiration = row.fieldValueOrNone<Instant>  "absolute_expiration" })
        match tryEntry with
        | Some entry ->
            let now      = getNow ()
            let slideExp = defaultArg entry.SlidingExpiration  Duration.MinValue
            let absExp   = defaultArg entry.AbsoluteExpiration Instant.MinValue
            let needsRefresh, item =
                if entry.ExpireAt = absExp then false, entry
                elif slideExp = Duration.MinValue && absExp = Instant.MinValue then false, entry
                elif absExp > Instant.MinValue && entry.ExpireAt.Plus slideExp > absExp then
                    true, { entry with ExpireAt = absExp }
                else true, { entry with ExpireAt = now.Plus slideExp }
            if needsRefresh then
                do! Custom.nonQuery
                        "UPDATE session SET expire_at = @expireAt WHERE id = @id"
                        [ expireParam item.ExpireAt; idParam ]
                ()
            return if item.ExpireAt > now then Some entry else None
        | None -> return None
    }
    
    /// The last time expired entries were purged (runs every 30 minutes)
    let mutable lastPurge = Instant.MinValue
    
    /// Purge expired entries every 30 minutes
    let purge () = backgroundTask {
        let now = getNow ()
        if lastPurge.Plus(Duration.FromMinutes 30L) < now then
            do! Custom.nonQuery "DELETE FROM session WHERE expire_at < @expireAt" [ expireParam now ]
            lastPurge <- now
    }
    
    /// Remove a cache entry
    let removeEntry key =
        Custom.nonQuery "DELETE FROM session WHERE id = @id" [ "@id", Sql.string key ]
    
    /// Save an entry
    let saveEntry (opts: DistributedCacheEntryOptions) key payload =
        let now = getNow ()
        let expireAt, slideExp, absExp =
            if opts.SlidingExpiration.HasValue then
                let slide = Duration.FromTimeSpan opts.SlidingExpiration.Value
                now.Plus slide, Some slide, None
            elif opts.AbsoluteExpiration.HasValue then
                let exp = Instant.FromDateTimeOffset opts.AbsoluteExpiration.Value
                exp, None, Some exp
            elif opts.AbsoluteExpirationRelativeToNow.HasValue then
                let exp = now.Plus(Duration.FromTimeSpan opts.AbsoluteExpirationRelativeToNow.Value)
                exp, None, Some exp
            else
                // Default to 1 hour sliding expiration
                let slide = Duration.FromHours 1
                now.Plus slide, Some slide, None
        Custom.nonQuery
            "INSERT INTO session (
                id, payload, expire_at, sliding_expiration, absolute_expiration
            ) VALUES (
                @id, @payload, @expireAt, @slideExp, @absExp
            ) ON CONFLICT (id) DO UPDATE
            SET payload             = EXCLUDED.payload,
                expire_at           = EXCLUDED.expire_at,
                sliding_expiration  = EXCLUDED.sliding_expiration,
                absolute_expiration = EXCLUDED.absolute_expiration"
            [   "@id",      Sql.string key
                "@payload", Sql.bytea payload
                expireParam expireAt
                optParam "slideExp" slideExp
                optParam "absExp"   absExp ]
        
    // ~~~ IMPLEMENTATION FUNCTIONS ~~~
    
    /// Retrieve the data for a cache entry
    let get key (_: CancellationToken) = backgroundTask {
        match! getEntry key with
        | Some entry ->
            do! purge ()
            return entry.Payload
        | None -> return null
    }
    
    /// Refresh an entry
    let refresh key (cancelToken: CancellationToken) = backgroundTask {
        let! _ = get key cancelToken
        ()
    }
    
    /// Remove an entry
    let remove key (_: CancellationToken) = backgroundTask {
        do! removeEntry key
        do! purge ()
    }
    
    /// Set an entry
    let set key value options (_: CancellationToken) = backgroundTask {
        do! saveEntry options key value
        do! purge ()
    }
    
    interface IDistributedCache with
        member _.Get key = get key CancellationToken.None |> sync
        member _.GetAsync(key, token) = get key token
        member _.Refresh key = refresh key CancellationToken.None |> sync
        member _.RefreshAsync(key, token) = refresh key token
        member _.Remove key = remove key CancellationToken.None |> sync
        member _.RemoveAsync(key, token) = remove key token
        member _.Set(key, value, options) = set key value options CancellationToken.None |> sync
        member _.SetAsync(key, value, options, token) = set key value options token
