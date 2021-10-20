open MyWebLog
open Suave

startWebServer defaultConfig (Successful.OK (Strings.get "LastUpdated"))
