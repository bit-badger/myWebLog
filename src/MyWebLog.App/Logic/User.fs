/// Logic for manipulating <see cref="User" /> entities
module MyWebLog.Logic.User

open MyWebLog.Data

/// Try to log on a user
let tryUserLogOn (data : IMyWebLogData) email passwordHash = data.LogOn email passwordHash

let setUserPassword (data : IMyWebLogData) = data.SetUserPassword