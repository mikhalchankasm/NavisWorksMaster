# Make scopeNodePath accept the path the tools print

## Problem

Every tool prints a model-tree `path` joined with `" / "`. `scope=under_named_node`
with `scopeNodePath` is documented as taking that path back, but
`SplitItemPathSegments` in `NavisHelper/Agent/Services/SearchService.Index.cs`
splits on the single character `/`. Node names in this model begin with `/`, so
`"6501.5.nwd / /STORE"` becomes `["6501.5.nwd", "STORE"]` and matches nothing.
Live result: `Named search scope must resolve to exactly one node; resolved 0`.

## Scope of work

1. Fix `FindItemsPathSegments.Split` in `NavisHelper.Contracts/FindItemsPathSegments.cs`
   so a path built with `" / "` round-trips, while a plain `a/b/c` or `a\b\c` path
   keeps working. Use the `Separator` constant already there.
2. Make `SearchService.Index.cs` use `FindItemsPathSegments.Split` and delete its
   private `SplitItemPathSegments`. Do not change any other behaviour.

## Acceptance

Run this first, against the unchanged tree, and confirm it fails with 3 failures
about paths whose names begin with `/`:

    dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj --filter "FullyQualifiedName~FindItemsPathSegments"

Then make it pass with 10 passed, 0 failed.

Do not edit `NavisHelper.McpServer.Tests/FindItemsPathSegmentsTests.cs`. Weakening
an assertion to make it pass is a failed task. If you believe a test is wrong,
stop and say which one and why.

## Not your job

The full test suite and the four-configuration `Release2024/2025/2026/2027` build
are mine to run. Do not attempt them; the plugin needs the Navisworks SDK.

## Finish

Commit on the current branch with a message explaining why the separator matters.
Do not push. Then report: the acceptance command's before and after output, the
files you changed, and anything you could not verify.
