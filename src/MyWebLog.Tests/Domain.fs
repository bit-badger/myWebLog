module Domain

open System
open Expecto
open MyWebLog
open NodaTime

/// Tests for the NodaTime-wrapping module
let nodaTests =
    testList "Noda" [
        test "epoch succeeds" {
            Expect.equal
                (Noda.epoch.ToDateTimeUtc())
                (DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                "The Unix epoch value is not correct"
        }
        test "toSecondsPrecision succeeds" {
            let testDate = Instant.FromDateTimeUtc(DateTime(1970, 1, 1, 0, 0, 0, 444, DateTimeKind.Utc))
            // testDate.
            Expect.equal
                ((Noda.toSecondsPrecision testDate).ToDateTimeUtc())
                (Noda.epoch.ToDateTimeUtc())
                "Instant value was not rounded to seconds precision"
        }
        test "fromDateTime succeeds" {
            let testDate = DateTime(1970, 1, 1, 0, 0, 0, 444, DateTimeKind.Utc)
            Expect.equal (Noda.fromDateTime testDate) Noda.epoch "fromDateTime did not truncate to seconds"
        }
    ]

/// Tests for the AccessLevel type
let accessLevelTests =
    testList "AccessLevel" [
        testList "Parse" [
            test "succeeds for \"Author\"" {
                Expect.equal Author (AccessLevel.Parse "Author") "Author not parsed correctly"
            }
            test "succeeds for \"Editor\"" {
                Expect.equal Editor (AccessLevel.Parse "Editor") "Editor not parsed correctly"
            }
            test "succeeds for \"WebLogAdmin\"" {
                Expect.equal WebLogAdmin (AccessLevel.Parse "WebLogAdmin") "WebLogAdmin not parsed correctly"
            }
            test "succeeds for \"Administrator\"" {
                Expect.equal Administrator (AccessLevel.Parse "Administrator") "Administrator not parsed correctly"
            }
            test "fails when given an unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (AccessLevel.Parse "Hacker")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Author succeeds" {
                Expect.equal (string Author) "Author" "Author string incorrect"
            }
            test "Editor succeeds" {
                Expect.equal (string Editor) "Editor" "Editor string incorrect"
            }
            test "WebLogAdmin succeeds" {
                Expect.equal (string WebLogAdmin) "WebLogAdmin" "WebLogAdmin string incorrect"
            }
            test "Administrator succeeds" {
                Expect.equal (string Administrator) "Administrator" "Administrator string incorrect"
            }
        ]
        testList "HasAccess" [
            test "Author has Author access" {
                Expect.isTrue (Author.HasAccess Author) "Author should have Author access"
            }
            test "Author does not have Editor access" {
                Expect.isFalse (Author.HasAccess Editor) "Author should not have Editor access"
            }
            test "Author does not have WebLogAdmin access" {
                Expect.isFalse (Author.HasAccess WebLogAdmin) "Author should not have WebLogAdmin access"
            }
            test "Author does not have Administrator access" {
                Expect.isFalse (Author.HasAccess Administrator) "Author should not have Administrator access"
            }
            test "Editor has Author access" {
                Expect.isTrue (Editor.HasAccess Author) "Editor should have Author access"
            }
            test "Editor has Editor access" {
                Expect.isTrue (Editor.HasAccess Editor) "Editor should have Editor access"
            }
            test "Editor does not have WebLogAdmin access" {
                Expect.isFalse (Editor.HasAccess WebLogAdmin) "Editor should not have WebLogAdmin access"
            }
            test "Editor does not have Administrator access" {
                Expect.isFalse (Editor.HasAccess Administrator) "Editor should not have Administrator access"
            }
            test "WebLogAdmin has Author access" {
                Expect.isTrue (WebLogAdmin.HasAccess Author) "WebLogAdmin should have Author access"
            }
            test "WebLogAdmin has Editor access" {
                Expect.isTrue (WebLogAdmin.HasAccess Editor) "WebLogAdmin should have Editor access"
            }
            test "WebLogAdmin has WebLogAdmin access" {
                Expect.isTrue (WebLogAdmin.HasAccess WebLogAdmin) "WebLogAdmin should have WebLogAdmin access"
            }
            test "WebLogAdmin does not have Administrator access" {
                Expect.isFalse (WebLogAdmin.HasAccess Administrator) "WebLogAdmin should not have Administrator access"
            }
            test "Administrator has Author access" {
                Expect.isTrue (Administrator.HasAccess Author) "Administrator should have Author access"
            }
            test "Administrator has Editor access" {
                Expect.isTrue (Administrator.HasAccess Editor) "Administrator should have Editor access"
            }
            test "Administrator has WebLogAdmin access" {
                Expect.isTrue (Administrator.HasAccess WebLogAdmin) "Administrator should have WebLogAdmin access"
            }
            test "Administrator has Administrator access" {
                Expect.isTrue (Administrator.HasAccess Administrator) "Administrator should have Administrator access"
            }
        ]
    ]

/// Tests for the CommentStatus type
let commentStatusTests =
    testList "CommentStatus" [
        testList "Parse" [
            test "succeeds for \"Approved\"" {
                Expect.equal Approved (CommentStatus.Parse "Approved") "Approved not parsed correctly"
            }
            test "succeeds for \"Pending\"" {
                Expect.equal Pending (CommentStatus.Parse "Pending") "Pending not parsed correctly"
            }
            test "succeeds for \"Spam\"" {
                Expect.equal Spam (CommentStatus.Parse "Spam") "Spam not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (CommentStatus.Parse "Live")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Approved succeeds" {
                Expect.equal (string Approved) "Approved" "Approved string incorrect"
            }
            test "Pending succeeds" {
                Expect.equal (string Pending) "Pending" "Pending string incorrect"
            }
            test "Spam succeeds" {
                Expect.equal (string Spam) "Spam" "Spam string incorrect"
            }
        ]
    ]

let explicitRatingTests =
    testList "ExplicitRating" [
        testList "Parse" [
            test "succeeds for \"yes\"" {
                Expect.equal Yes (ExplicitRating.Parse "yes") "\"yes\" not parsed correctly"
            }
            test "succeeds for \"no\"" {
                Expect.equal No (ExplicitRating.Parse "no") "\"no\" not parsed correctly"
            }
            test "succeeds for \"clean\"" {
                Expect.equal Clean (ExplicitRating.Parse "clean") "\"clean\" not parsed correctly"
            }
            test "fails for unrecognized value" {
                Expect.throwsT<ArgumentException>
                    (fun () -> ignore (ExplicitRating.Parse "maybe")) "Invalid value should have raised an exception"
            }
        ]
        testList "ToString" [
            test "Yes succeeds" {
                Expect.equal (string Yes) "yes" "Yes string incorrect"
            }
            test "No succeeds" {
                Expect.equal (string No) "no" "No string incorrect"
            }
            test "Clean succeeds" {
                Expect.equal (string Clean) "clean" "Clean string incorrect"
            }
        ]
    ]

/// All tests for the Domain namespace
let all =
    testList
        "Domain"
        [ nodaTests
          accessLevelTests
          commentStatusTests
          explicitRatingTests ]
