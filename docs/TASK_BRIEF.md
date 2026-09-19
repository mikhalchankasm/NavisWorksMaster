# Task brief template

Copy this into the Issue. Maximum 2 KB. If it does not fit, the task is too big —
split it.

```markdown
## Outcome
<One sentence: what exists after the task that does not exist now.>

## Boundaries
- In scope: <the narrowest useful end-to-end slice>
- Out of scope: <what must not be touched, and what this must not generalize into>

## Acceptance
<How the owner will check it. One observable check. Technical-only, or does it
need engineering acceptance on a live model?>

## Files
<Expected files to change. If unknown, the entry point.>

## Verify
<The exact L0 command for this task.>

## Links
<At most three. There is no such thing as a link "for context".>
```

## Notes

`Acceptance` is the field that decides whether the task can be closed without a
live Navisworks session. Say which it is; do not leave it implied.

`Verify` is a command someone else can paste. `dotnet test ...` with no filter is
not a task-specific check.

A brief with an empty `Boundaries` section is not ready. "Out of scope" is what
stops the second attempt from growing into a rewrite.
