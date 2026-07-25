# Persistent User Scenario Library Contract Proposal

## Status

Implemented contract for the MCP-server scenario library. The implementation exposes list/get/save/delete/resolve tools, while export/import remains deferred. Existing MCP tool names, host commands, plugin IDs, and Navisworks host wire contracts remain unchanged.

## MVP decision

The first release stores, inspects, validates, resolves, and deletes user-approved scenarios. Export/import is a later portability slice. It does not provide a `run_scenario` tool.

After resolving a scenario, the MCP client or assistant invokes every referenced existing MCP tool separately. Each invocation therefore keeps its existing validation, dry-run, confirmation, logging, timeout, and error behavior. A scenario cannot grant permission that the underlying tool call does not already have.

The library supports two user-selected modes:

- `template`: reusable steps with values that may be requested or changed for each run.
- `exactReplay`: a reviewed snapshot of the complete permitted step order, arguments, fixed parameter values, path values, and behavioral write policy. A direct user command to replay that named scenario is the current authorization to run it without follow-up questions.

Exact replay guarantees repetition of the saved procedure and settings, not identical model results or unconditional completion. It has a no-question guarantee: replay either performs the saved procedure or stops at the first blocker and reports it. It never asks a clarifying question and never silently falls back to template mode. If the model, files, tool contract, or runtime state changed, the same procedure may produce different counts or outputs.

## Schema version 2

Schema v2 extends the declarative workflow model; schema v1 remains accepted
unchanged and is never silently rewritten.

- `select_by_search` changes only the current selection. It reuses `find_items`
  conditions and supports `whole_model`, `direct_children_of`, and
  `descendants_of`. Parent-scoped searches require `parentConditions` to resolve
  exactly one item and fail closed at `maxMatchedItems`.
- `clash_create_matrix_from_selection` contract v2 accepts
  `pairNameTemplate`/`pairNameStartIndex`. Tokens are `{index}`, `{aName}`,
  `{bName}`, `{aCode}`, `{bCode}`. Transforms are `zeroPad:N`,
  `strip:#regex#`, `replace:#regex#replacement#`, `upper`, and `lower`.
  `clash_manage_tests.namePattern` uses the same grammar.
- `clash_bbox_pair_plan` contract v3 accepts `sourceMode:"selection"` for
  arbitrary selected nested groups.
- Parameters formally support `string`, `int`, `number`, `bool`, and `enum`,
  plus `default`, string `pattern`, and string `enum[]`. Legacy type names and
  path/list types remain supported.
- `{"$stepResult":"priorStep.output"}` may reference only a prior step and an
  output projection published by `scenario_capabilities`. Forward references,
  arbitrary result fields, and runtime handles remain forbidden.
- A bounded `foreach` requires an allowlisted `$stepResult` in `over`,
  `maxIterations` from 1 to 1000, and 1 to 16 body steps. Nesting is forbidden;
  body values use `{"$loop":"item.field"}`.
- A template may store reviewed behavior such as
  `removePreviousGenerated:true` only when the step declares
  `reviewedWrites:["removePreviousGenerated"]`. The declaration is checked
  against that tool's allowlist, appears in the resolved write plan, and never
  bypasses the normal preview/apply gate.

The complete current example returned by `scenario_capabilities` performs
`select_by_search(direct_children_of)` → matrix build/run with clean pair names
and reviewed idempotent cleanup → delete tests scoped by `namePrefix` and
`onlyWithTotal:0`.

## Storage layout

```text
%APPDATA%\NavisHelper\Scenarios\
  <scenario-id>.json
```

- A scenario is stored as one UTF-8 JSON file without a byte-order mark.
- `scenarioId` is a lowercase UUID and is also the filename stem. User-supplied names never become filenames.
- Writes use a temporary file in the same directory, flush it, and atomically replace the target.
- The store rejects reparse points and files whose resolved path escapes the scenario root.
- The scenario directory is created only by the user's first confirmed save or import. Installation and startup do not create it or add samples.
- The installer, updater, and uninstaller do not create, migrate, or delete the scenario root.
- Import and export operate only on explicit user-selected files. Export is the backup and profile-migration mechanism.
- The MVP limits the store to 500 scenario files, each no larger than 256 KiB. Listing ignores unrelated files and reports oversized scenario files as invalid without parsing them.

## JSON schema version 1

The persisted document uses camelCase independently of the existing host protocol's snake_case payloads.

```json
{
  "schemaVersion": 1,
  "scenarioId": "7a78dd43-6e14-4c29-bc1d-7eb48ec3422b",
  "executionMode": "template",
  "name": "Отчёт по выбранным коллизиям",
  "description": "Создать виды и выгрузить отчёт в папку проекта.",
  "createdUtc": "2026-07-15T10:30:00Z",
  "updatedUtc": "2026-07-15T10:30:00Z",
  "context": {
    "navisworksVersions": ["2026", "2027"],
    "rootFilePatterns": ["AR_*.nwc", "KR_*.nwc"],
    "projectLabel": "Северный терминал"
  },
  "parameters": [
    {
      "name": "reportDirectory",
      "type": "directoryPath",
      "title": "Папка отчёта",
      "required": true
    },
    {
      "name": "testPrefix",
      "type": "string",
      "title": "Префикс тестов",
      "required": true,
      "default": "АР-КР"
    }
  ],
  "steps": [
    {
      "stepId": "preview-report",
      "tool": "clash_generate_report",
      "arguments": {
        "testNamePrefix": { "$parameter": "testPrefix" },
        "outputDirectory": { "$parameter": "reportDirectory" }
      }
    }
  ]
}
```

### Root fields

| Field | Rule |
|---|---|
| `schemaVersion` | Required integer. Version 1 is the only accepted persisted version in the MVP. |
| `scenarioId` | Required UUID assigned once by the store. It cannot be changed by update or import collision handling. |
| `executionMode` | Required `template` or `exactReplay`. Changing it requires a new save preview and confirmation. |
| `name` | Required trimmed, printable, single-line display name, 1-120 Unicode characters. Template names may repeat; exact-replay names must be unique under ordinal case-insensitive comparison so a direct replay command always resolves one scenario. |
| `description` | Optional, maximum 1000 characters. |
| `createdUtc` | Required UTC RFC 3339 timestamp, maintained by the store. |
| `updatedUtc` | Required UTC RFC 3339 timestamp, maintained by the store. |
| `context` | Optional advisory matching hints. It never authorizes execution. |
| `parameters` | Required array with 0-32 names unique under ordinal case-insensitive comparison. References preserve and require the declared casing. |
| `steps` | Required ordered array with 1-32 steps. |

Unknown fields are rejected when saving or importing. This catches misspellings and prevents a newer schema from being silently weakened by an older runtime. Loading a hand-edited version-1 file reports unknown fields as an invalid-scenario error and leaves the file untouched. Every additive persisted field requires a new schema version; there are no same-version optional extensions.

Parsing uses `System.Text.Json` with explicit DTOs, no polymorphic type metadata or custom type activation, a maximum depth of 32, and strict token types. Both save/import validation and normal reads use the same parser and limits in the .NET 9 MCP server. The Navisworks plugin does not parse scenario files in the MVP.

### Parameters

Parameter names must match `^[A-Za-z][A-Za-z0-9_]{0,63}$` and are unique under ordinal case-insensitive comparison. MVP types are:

- `string`
- `integer`
- `number`
- `boolean`
- `filePath`
- `directoryPath`
- `stringList`

`title`, `description`, `required`, and `default` are optional metadata fields. Defaults must match the declared type. `filePath` and `directoryPath` parameters cannot have defaults. Secrets and sensitive parameters are deliberately unsupported in schema version 1 because existing downstream tool logging cannot guarantee end-to-end redaction. An operation that requires a credential in its MCP input is not scenario-eligible.

In `template` mode, paths must be parameters of type `filePath` or `directoryPath`. Literal absolute paths, UNC paths, device paths, environment-variable expressions, home-directory shortcuts, and relative path fragments are rejected inside step arguments. Version 1 has no path concatenation syntax: the user supplies or confirms the complete path for each resolution, and that runtime value is never persisted.

An `exactReplay` document additionally contains an `exactReplay` object:

```json
{
  "fixedParameters": {
    "reportDirectory": "D:\\Projects\\North\\Reports",
    "testPrefix": "АР-КР"
  },
  "contextPolicy": "strict",
  "writePolicy": "repeatReviewedWrites",
  "safetyEnvelope": {
    "previewFingerprint": "sha256:...",
    "stepLimits": {
      "preview-report": {
        "maxMatchedItems": 250,
        "maxModelWrites": 250,
        "maxFileWrites": 252,
        "approvedScaleGates": []
      }
    }
  }
}
```

- Every declared parameter must have one fixed value; the replay never prompts for missing values.
- Fixed values use their declared JSON types and are included in the save preview.
- Absolute local or UNC paths are allowed only here as explicitly reviewed fixed values. Environment expansions, device paths, credentials in UNC paths, and traversal segments remain forbidden.
- `contextPolicy` is always `strict` in schema version 1.
- `writePolicy` is `repeatReviewedWrites`: the later explicit replay command authorizes only the model/file writes shown when the exact scenario was saved. It does not authorize a newly introduced kind of write.
- `safetyEnvelope.previewFingerprint` is a server-generated SHA-256 of the canonical resolved exact plan (ordered tools, contract versions, fixed arguments, and forced dry-run values). It detects hand edits or plan drift. The envelope also records explicit per-step ceilings for matched items, model writes, file writes, and scale-confirmation gates already crossed. Defaults equal the reviewed preview counts; the user may approve higher ceilings only while saving or updating the exact scenario.
- Authorization gates such as `apply` and ordinary `confirm_*` are never persisted. The client derives them from the current explicit replay request after a successful preflight. Scale gates such as `confirmLargeReport` may be derived only when the same gate was explicitly recorded in `approvedScaleGates` and current counts remain inside the stored ceilings.

### Steps

- `stepId` must be unique and match the parameter-name character policy.
- `tool` must be an existing MCP tool on an explicit scenario allowlist.
- `arguments` is a JSON object validated against a scenario-specific form of the current tool input schema.
- A top-level argument references a declared parameter only with the exact JSON object
  `{"$parameter":"parameterName"}`. String templates such as `{{parameterName}}` are
  not parameter references. Nested parameter references are not supported in schema version 1.
- `stepId` must match `^[A-Za-z][A-Za-z0-9_]{0,63}$`; hyphens are not allowed.
- Stored arguments cannot contain authorization fields such as `apply`, `confirm_*`, or an operation-specific equivalent. The allowlist descriptor for each tool defines these fields and the resolver injects safe preview defaults. A behavioral setting such as overwrite is storable only when its allowlist entry classifies it as `reviewedWriteBehavior`, and then only in `exactReplay` after prominent review.
- A parameter reference is the complete JSON object `{ "$parameter": "parameterName" }`. The resolver substitutes its declared JSON type, so numbers, booleans, arrays, and strings remain type-correct.
- Save/import validation replaces each parameter reference with a representative value of its declared type before checking the current tool input schema. A type incompatible with the target property is rejected.
- Every reference must name a declared parameter using exact case. Every required parameter must be referenced by at least one step. Duplicate or undeclared references and reference objects with extra fields are rejected.
- Plain strings such as `"${name}"` have no special meaning. Object keys cannot contain references, and concatenation/interpolation is not supported in version 1.
- Outputs from one step cannot become inputs to another step in version 1. This avoids persisting transient handles and implicit control flow.
- Conditions, loops, retries, scripts, commands, expressions, and arbitrary executable content are forbidden.

For `exactReplay`, each step also stores the allowlist entry's `scenarioContractVersion`. Any change to the scenario-visible meaning, storable arguments, preview override, or write classification of that operation increments this version. A mismatch blocks exact replay instead of silently adapting the scenario.

All persisted/displayed text fields, including names, descriptions, titles, project labels, patterns, step IDs, and literal string arguments, reject C0/C1 controls, CR/LF, ANSI escape, and Unicode bidi-override/isolate controls. Normal Russian and other printable Unicode text remains supported.

## Operation allowlist

The allowlist is maintained in code, versioned with the MCP server, and defaults to deny. Each entry identifies the current tool, storable arguments, forbidden authorization arguments, and safe preview overrides. A tool is eligible only when all of the following are true:

1. Its public input contract can be validated without calling Navisworks.
2. Its arguments do not require persisted document IDs, instance IDs, PIDs, model item handles, or credentials.
3. Any mutation already supports a dry-run or preview mode, and the allowlist entry can force that mode independently of persisted data.
4. The operation remains meaningful when executed as a separate normal MCP call.
5. For `exactReplay`, the operation exposes a bounded dry-run response whose relevant counts and write categories can be compared with the saved safety envelope. Existing tool validation and large-operation gates remain final authority.

The allowlist includes the read-only model/selection inspection tools, the basic
selection-set → isolate → zoom navigation workflow, and the scenario-safe Clash
Detective block. Mutating tools remain forced to their dry-run argument during
resolution. `scenario_capabilities` returns the complete current list, contract
versions, parameter syntax, step-id pattern, and a valid example. `list_scenarios`
and failed `save_scenario` responses also expose the current tool names.

The bbox/clash workflow can be expressed without persisted runtime handles:

- `clash_bbox_pair_plan.targetRootName` keeps only pairs containing one exact root;
- `clash_manage_tests.onlyWithTotal=0` scopes empty tests under a stable name prefix;
- `clash_manage_tests.namePattern` supports `{index}`, `{name}`, `{aName}`, and
  `{bName}` for handle-free batch renaming;
- `clash_pair_tests_create.settingsFromTestName` copies type/tolerance from an
  existing project test when explicit settings are omitted.

The allowlist classifies every argument as one of:

- `input`: safe workflow behavior that may be fixed in either mode.
- `reviewedWriteBehavior`: behavior such as replacing a known output that may be fixed only in `exactReplay` and must appear prominently in its save preview.
- `authorization`: ordinary `apply`, `confirm_*`, and equivalent consent gates that are never stored and may be derived from the current replay request.
- `scaleAuthorization`: large-operation consent gates that are never stored as arguments and may be derived only when the saved safety envelope already contains the same gate and the current scope stays within its ceilings.
- `runtimeIdentity`: instance, process, document, and model-item identities that are never stored.

The store always rejects these argument names wherever they occur, using case-insensitive comparison:

- `instanceId`
- `pid`
- `documentId`
- `documentHandle`
- `matchHandle`
- `matchHandles`
- `itemId`
- `itemIds`
- `apiKey`
- `token`
- `authorization`

It also normalizes property names by removing separators and comparing case-insensitively, then rejects credential terms such as `secret`, `password`, `credential`, `apikey`, `accesstoken`, `bearertoken`, and `clientsecret`. Literal values with explicit credential prefixes such as `Bearer ` or known API-key prefixes are rejected; a generic high-entropy string produces a warning rather than an automatic rejection. Raw JSON/string payload parameters are not eligible for the allowlist. These checks are defense in depth and do not replace per-tool storable-argument definitions or the redacted save preview.

Model/selection-set names and project labels may be confidential. Scenario documents remain local to the Windows profile and are never sent to telemetry, the AI color endpoint, or another service by the scenario library. A client may receive a redacted scenario only through an explicit MCP call.

## Implemented MCP tools

These names and contracts are proposals and must be approved before implementation.

### `list_scenarios`

Read-only metadata lookup. It never returns steps, parameter defaults, or resolved arguments.

Inputs:

- `query`: optional name/description substring.
- `navisworks_version`: optional matching hint.
- `root_file_names`: optional bounded list of current root filenames.
- `project_label`: optional exact user-provided label.
- `limit`: default 3, range 1-20.

Output entries include `scenario_id`, `name`, `description`, `updated_utc`, a match grade (`strong`, `partial`, `weak`, or `mismatch`), and human-readable match reasons. A mismatch may still be displayed but is never silently selected.

### `get_scenario`

Read-only retrieval by `scenario_id`. Returns the validated persisted document plus status and warnings. Schema version 1 cannot contain sensitive parameter values. It does not resolve parameters.

### `save_scenario`

Creates or updates a scenario only after a preview/apply sequence.

Inputs:

- `scenario`: proposed schema-version-1 document. For creation, store-owned identity and timestamp fields are omitted.
- `scenario_id`: optional existing ID for update.
- `expected_sha256`: required for update to prevent lost writes and ABA within timestamp resolution.
- `apply`: default `false`; validation and redacted preview only.
- `confirm_save`: required `true` with `apply=true`.

Apply returns the stored ID, timestamp, path below the scenario root, and content SHA-256. Template saves never accept or echo one-time runtime parameter values; exact-replay fixed values are part of the reviewed scenario document.

Saving `exactReplay` requires a distinct `confirm_exact_replay=true` in addition to `confirm_save=true`. The preview lists every fixed value, absolute/UNC path, model mutation, file write, overwrite behavior, and the rule that future direct replay commands will not ask follow-up questions.

### `delete_scenario`

Defaults to preview. Apply requires `confirm_delete=true` and `expected_sha256`. Deletion removes exactly one scenario file and does not recursively delete directories.

### `resolve_scenario`

Read-only. Accepts `scenario_id`, a parameter-value object for template mode, an optional current `navisworks_version` hint, and `execution_intent=preview|exact_replay`. It validates types, required values, current MCP tool availability, the scenario allowlist, and the current tool input schemas. It returns an ordered preview call plan and a summary of all potential model/file writes.

Resolution does not call any scenario step. The client must show the plan to the user and then invoke the existing tools one by one. Mutating step arguments remain in preview mode unless the user separately approves the underlying tool's apply call.

Resolve never contacts a Navisworks host. Without a version hint, version-specific host availability is `unknown`; with a hint, it is checked against static compatibility metadata shipped with the MCP server. If a tool or parameter was removed, renamed, or is known to be unavailable for that version, resolution returns a stale/unsupported validation result and no executable call plan. It never guesses a replacement. The scenario file remains unchanged until the user explicitly edits or resaves it.

For `exact_replay`, resolve accepts no parameter overrides. It returns the stored fixed plan, its scenario SHA-256, operation contract versions, strict context requirements, reviewed write summary, and safe dry-run calls. It does not execute them.

### `export_scenarios` and `import_scenarios`

These are optional for the first implementation slice but required before calling the library portable.

- Export writes a versioned ZIP containing scenario JSON and a manifest with SHA-256 hashes. It excludes runtime logs and client configuration.
- Export confirmation warns that names, descriptions, project labels, root-file patterns, and literal workflow arguments may contain confidential project information. Encryption is outside the MVP.
- Import defaults to preview, rejects invalid or unsupported schemas, and reports conflicts.
- Apply requires explicit confirmation and a conflict policy: `skip`, `replace_if_unchanged`, or `copy_with_new_id`.
- Imported timestamps are retained for `skip`/unchanged comparisons; a copied scenario receives a new ID and timestamps.
- Import accepts only a regular-file manifest and regular `<uuid>.json` entries. It rejects absolute/traversing names, directories, links/reparse entries, alternate data streams, unexpected entries, duplicate entry names, duplicate IDs inside the archive, more than 100 scenarios, entries over 256 KiB, total uncompressed content over 25 MiB, and a compression ratio over 100:1. Extraction is streamed to validated temporary files and never trusts archive paths.
- An imported ID colliding with the existing store is not an archive duplicate: `skip` keeps the store copy, `copy_with_new_id` creates a new identity, and `replace_if_unchanged` replaces only when the current store SHA-256 equals the baseline store hash recorded in the export manifest. Missing or mismatched baselines cause a conflict with no write.

## Matching policy

Matching is advisory and deterministic:

- `strong`: Navisworks version matches and every stored root-file pattern matches at least one current root filename; project label also matches when stored.
- `partial`: at least one provided hint matches and none explicitly conflict.
- `weak`: the scenario or request has insufficient context.
- `mismatch`: a stored Navisworks version or project label conflicts, or no stored root-file pattern matches any supplied root filename.

Only metadata is needed for matching. The assistant should offer no more than three suggestions by default and must identify partial, weak, or mismatched context in user-facing text.

## Authorization and execution

1. Saving, updating, deleting, importing, and exporting are explicit file mutations and therefore use preview/apply plus confirmation.
2. Listing, reading, validating, matching, and resolving are read-only.
3. Resolving is not consent to execute.
4. Every resolved step is invoked through the existing MCP tool contract. Scenario storage cannot force `apply=true`, satisfy confirmation flags, or suppress a tool preview.
5. In template mode, the user sees current model mutations and file writes before execution. In exact-replay mode, these are reviewed when the scenario and its safety envelope are saved or updated; the replay preflight must remain inside that envelope.
6. A failure stops the suggested sequence. Resume starts from a newly resolved plan; the scenario does not persist execution state.
7. No scenario runs on startup, file open, timer, filesystem event, or background task.
8. A file planted or modified by another local process has no authority. It is parsed as untrusted input and cannot pre-authorize a mutation.

### Exact replay without follow-up questions

A request such as “полностью повтори сценарий «Отчёт АР-КР»” or “запусти точный сценарий «Отчёт АР-КР»” is explicit current authorization for that one replay. It is not a background or standing execution trigger.

The client then performs this sequence without asking questions:

1. Resolve one exact scenario by unique case-insensitive name or explicit ID and record its ID and SHA-256.
2. Verify the strict context, fixed paths, tool availability, operation contract versions, and absence of new write categories.
3. Verify the server-generated canonical plan fingerprint, then invoke each mutating operation in its normal dry-run mode and obtain its current counts/write summary.
4. Verify that the preview remains within the saved write policy, per-step ceilings, and previously approved scale gates.
5. Recheck the scenario SHA-256 and document identity, then invoke apply. Derive ordinary authorization gates from the current replay request; derive a scale gate only when step 4 proved it was previously approved and remains within its ceiling.
6. Stop on the first error and report what completed and what did not.

The client must stop without applying changes when the scenario is stale, a fixed path is unavailable, strict context does not match, an operation contract changed, the canonical plan fingerprint is rejected, the scenario or document changed after preview, any count exceeds its stored ceiling, a new scale gate is crossed, the preview introduces an unreviewed write category, or an existing tool requires a new kind of consent that was not part of the saved exact scenario. It reports the blocker rather than falling back to questions or silently switching to template mode.

Schema version 1 does not add an atomic preview-to-apply scope token to existing tools. A user or another process may change the Navisworks document between those calls. Exact replay reduces this risk through immediate sequential execution, scenario/document rechecks, existing tool validation, and unchanged large-operation gates; it does not claim transactional isolation.

The audit log records the scenario ID, scenario SHA-256, exact-replay intent, resolved tool names, previews, applications, and outcome. It does not copy the scenario file or confidential fixed values into the log.

## Migration policy

- Version 1 readers accept only `schemaVersion: 1`.
- Listing first reads a bounded JSON envelope containing `schemaVersion`, ID, name, and timestamps. A well-formed newer version is reported as `unsupported_schema` without applying version-1 unknown-field rules; corrupt or unsafe envelopes are reported separately as invalid.
- A future reader may migrate an older document only through a pure, deterministic, tested function.
- Migration first creates a sibling backup in an explicit user-approved export; opening or listing never rewrites a document.
- Downgrade is unsupported. A newer unknown schema is reported as `unsupported_schema` and remains untouched.
- Schema migration changes persisted structure. Allowlist changes do not.
- Navisworks 2024-2027 share the same per-user store. Version-specific tool unavailability is reported during resolution; it does not crash Navisworks or rewrite the scenario.

## Retention and recovery

- No automatic expiration or cleanup is performed.
- Updates use optimistic concurrency through the last returned content SHA-256 and atomic replacement. Timestamps are display metadata, not concurrency tokens.
- Malformed files are reported but not renamed or deleted automatically.
- Duplicate IDs within one import archive, filename/ID mismatch, and manifest hash mismatch are hard errors. Collisions with existing store IDs follow the explicit import conflict policy.
- The user can delete individual scenarios or explicitly export and then remove all scenarios in a future UI; the MVP has no recursive `delete_all` operation.
- All user-facing validation, stale-contract, mismatch, confirmation, and recovery messages are localized in Russian. Raw parser exceptions are never shown directly to the user.
- At the 500-file store limit, list/get/resolve and save previews continue to work, but a new save or copying import fails before writing with a localized `scenario_store_full` error. Updating an existing scenario remains allowed.
- Export/import always uses an explicit user-selected path outside the managed store; there is no implicit export directory.

## Required tests before exposing tools

- JSON round-trip, strict unknown-field rejection, size/count limits, and all parameter types.
- Hardened parser depth/type tests and hostile polymorphic metadata tests.
- Parameter-reference validation and rejection of inline interpolation.
- Typed parameter-reference substitution, exact-case lookup, dangling required-parameter, and target-schema compatibility tests.
- Exact-replay completeness, case-insensitive unique-name enforcement, fixed path review, strict context, write-category drift, operation contract version, canonical plan-fingerprint rejection, saved count ceilings, scale-gate drift, and no-prompt stop-policy tests.
- Operation allowlist and forbidden-argument tests.
- Absolute, UNC, device, traversal, environment-variable, and reparse-point path tests.
- ZIP slip, link entry, duplicate entry/ID, decompression ratio, entry count, and expanded-size tests.
- Atomic create/update, optimistic-concurrency conflict, malformed file, duplicate ID, and interrupted-write recovery tests.
- Metadata-only matching grades and bounded result ordering.
- Redaction in previews, responses, exceptions, and MCP JSONL logs.
- Resolve proves that it never invokes a host command.
- Existing MCP catalog/tool contract guard remains stable except for explicitly approved new tools.
- Installer/update/uninstall tests prove `%APPDATA%\NavisHelper\Scenarios` is preserved.
- Navisworks-version/tool-contract drift returns a localized stale result without rewriting the file.

## Implementation status and remaining slices

1. Implemented: pure .NET 9 MCP-server schema, validator, allowlist, resolver, and unit tests with no plugin dependency.
2. Implemented: atomic per-user store plus list/get/save/delete tools and contract tests.
3. Implemented: resolve tool, matching suggestions, exact-replay instructions, and stdio MCP smoke coverage.
4. Remaining: export/import with conflict handling and package preservation tests.
5. Required before release: full non-Navisworks test suite, tool-less Claude review, and distribution validation; Release2024-Release2027 x64 plus live NWD smoke are required if plugin/bundle code changes.

## Approved decisions

1. The MVP has no `run_scenario` tool.
2. It supports `template` and `exactReplay`, including one-command replay without follow-up questions when strict preflight succeeds.
3. Scenario file mutations use preview/apply confirmation.
4. The operation allowlist is explicit and deny-by-default, but now includes the safe read-only/navigation tools and scenario-safe Clash Detective workflows exposed by `scenario_capabilities`.
5. Export/import is deferred to the next slice.
`clash_tests_from_sets` and `clash_run_batch` are scenario-allowlisted with
dry-run/apply review. The former uses a stable set path or unique name in saved
scenarios (`itemId` remains a forbidden runtime identity) and exposes `tests`, `createdTestCount`, and
`runOperationId` projections. Runtime operation IDs and Clash handles remain
ephemeral and must not be stored as fixed scenario parameters.
