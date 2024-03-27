module DataTypesTests

open Expecto
open MyWebLog

/// Unit tests for the WebLog type
let webLogTests = testList "WebLog" [
    testList "ExtraPath" [
        test "succeeds for blank URL base" {
            Expect.equal WebLog.Empty.ExtraPath "" "Extra path should have been blank for blank URL base"
        }
        test "succeeds for domain root URL" {
            Expect.equal
                { WebLog.Empty with UrlBase = "https://example.com" }.ExtraPath
                ""
                "Extra path should have been blank for domain root"
        }
        test "succeeds for single subdirectory" {
            Expect.equal
                { WebLog.Empty with UrlBase = "https://a.com/sub" }.ExtraPath
                "/sub"
                "Extra path incorrect for a single subdirectory"
        }
        test "succeeds for deeper nesting" {
            Expect.equal
                { WebLog.Empty with UrlBase = "https://b.com/users/test/units" }.ExtraPath
                "/users/test/units"
                "Extra path incorrect for deeper nesting"
        }
    ]
    test "AbsoluteUrl succeeds" {
        Expect.equal
            ({ WebLog.Empty with UrlBase = "https://my.site" }.AbsoluteUrl(Permalink "blog/page.html"))
            "https://my.site/blog/page.html"
            "Absolute URL is incorrect"
    }
    testList "RelativeUrl" [
        test "succeeds for domain root URL" {
            Expect.equal
                ({ WebLog.Empty with UrlBase = "https://test.me" }.RelativeUrl(Permalink "about.htm"))
                "/about.htm"
                "Relative URL is incorrect for domain root site"
        }
        test "succeeds for domain non-root URL" {
            Expect.equal
                ({ WebLog.Empty with UrlBase = "https://site.page/a/b/c" }.RelativeUrl(Permalink "x/y/z"))
                "/a/b/c/x/y/z"
                "Relative URL is incorrect for domain non-root site"
        }
    ]
    testList "LocalTime" [
        test "succeeds when no time zone is set" {
            Expect.equal
                (WebLog.Empty.LocalTime(Noda.epoch))
                (Noda.epoch.ToDateTimeUtc())
                "Reference should be UTC when no time zone is specified"
        }
        test "succeeds when time zone is set" {
            Expect.equal
                ({ WebLog.Empty with TimeZone = "Etc/GMT-1" }.LocalTime(Noda.epoch))
                (Noda.epoch.ToDateTimeUtc().AddHours 1)
                "The time should have been adjusted by one hour"
        }
    ]
]

/// Unit tests for the WebLogUser type
let webLogUserTests = testList "WebLogUser" [
    testList "DisplayName" [
        test "succeeds when a preferred name is present" {
            Expect.equal
                { WebLogUser.Empty with
                    FirstName = "Thomas"; PreferredName = "Tom"; LastName = "Tester" }.DisplayName
                "Tom Tester"
                "Display name incorrect when preferred name is present"
        }
        test "succeeds when a preferred name is absent" {
            Expect.equal
                { WebLogUser.Empty with FirstName = "Test"; LastName = "Units" }.DisplayName
                "Test Units"
                "Display name incorrect when preferred name is absent"
        }
    ]
]

/// All tests for the Domain.DataTypes file
let all = testList "DataTypes" [ webLogTests; webLogUserTests ]
