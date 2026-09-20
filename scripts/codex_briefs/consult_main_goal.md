# Consult: where to spend the next effort on MCP quality

Read only. Change no file, commit nothing.

Read `docs/MCP_TOOL_CONTRACTS.md` and `docs/ARCHITECTURE.md` first, and skim
`NavisHelper/Agent/Services/SearchService.*.cs`. Ground your answer in that code.

## Where we are

Two processes: a .NET 9 stdio MCP server and a net48 in-process Navisworks plugin,
over named pipes. `find_items` dominated both time and errors: measured 83% of tool
time and a 13% error rate across a week of calls.

Landed today, each measured live on an 88k-node model:

- tool profiles cut the per-request manifest from 53 300 to ~32 900 tokens
- matches keyed by `ModelItem` instead of a display-name path: 70 -> 115 real
  matches, same wall time
- scoped `matchDepth=first` answered by the engine with pruning on: a query that
  failed at the 45 s budget now returns in 318 ms
- an abandoned traversal now releases its ~270k item wrappers: the next search
  went from 7448 ms back to 453 ms
- `whole_model` + `countOnly` answered by the engine: 45 s failure -> 404 ms

Known and being fixed now: `scopeNodePath` does not accept the path the tools
print, because the join is `" / "` and the split is on `/` while node names begin
with `/`.

## The question

The goal is every tool fast *and* correct, not just the hot one.

Answer these three, concretely, in at most 400 words total:

1. Ranked: the three highest-leverage changes left, and for each the one
   observable fact that would prove it worked.
2. What would you check first that we appear not to have checked at all? Name the
   file or the call.
3. Which of the measured wins above looks most likely to be hiding a defect, and
   what would you run to find it?

Prefer naming a specific method or contract over general advice.
