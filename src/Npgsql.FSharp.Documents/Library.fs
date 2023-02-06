module Npgsql.FSharp.Documents


/// Query construction functions
module Query =
    
    /// Create a parameter for a @> (contains) query
    let contains<'T> (name : string) (value : 'T) =
        name, Sql.jsonb (string value) // FIXME: need a serializer
    
