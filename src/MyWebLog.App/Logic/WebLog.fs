/// Logic for manipulating <see cref="WebLog" /> entities
module MyWebLog.Logic.WebLog

open MyWebLog.Data
open MyWebLog.Entities

/// Find a web log by its URL base
let tryFindWebLogByUrlBase (data : IMyWebLogData) urlBase = data.WebLogByUrlBase urlBase

/// Find the counts for the admin dashboard
let findDashboardCounts (data : IMyWebLogData) webLogId = data.DashboardCounts webLogId
