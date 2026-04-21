# Mutation Check Protocol

Governs how `/implement-next-task` runs mutation testing on safety-critical code and how to handle surviving mutants.

The mutation check is a **hard gate**: Stryker.NET is configured with `break: 100`, so the run fails if any mutant survives or lacks coverage. Every mutant must be either killed by a test or explicitly ignored in source with a Stryker comment or the `[ExcludeFromCodeCoverage]` attribute.

## When to run

Run the mutation check when all of these hold:

- `dotnet test` passed in the current workspace
- The current diff against the target branch (default `main`) touches files under any configured safety-critical project. Initial set:
  - `src/ServantClaw.Domain/**`
  - `src/ServantClaw.Application/**`
- The current task is not a pure documentation or non-code change

If no safety-critical project is touched by the diff, skip the mutation check and say so explicitly in the final response.

Pre-gate with `git diff --name-only <base>...HEAD` before invoking Stryker. Stryker itself does not skip runs when the project-under-test has no changes — it will still build and instrument. The pre-gate is load-bearing.

## How to run

For each touched safety-critical project:

1. `cd` to the project-under-test directory (e.g. `src/ServantClaw.Domain`)
2. Invoke the pinned tool: `dotnet dotnet-stryker --since:main`
3. Wait for completion. HTML and JSON reports land in `StrykerOutput/<timestamp>/reports/`; a cleartext summary is printed.

Stryker exits with a non-zero code when the score is below `break: 100`, which fails the check. If the current branch has no `main` parent (detached HEAD, unusual rebase state), fall back to `--since:HEAD~1`, or skip the check with an explicit note. Do not treat `since` resolution errors as a blocker.

## How to handle surviving mutants

The run must reach 100% mutation score for the check to pass. For every surviving mutant, pick exactly one of three outcomes.

### Kill it with a test

Default outcome. Choose this when the mutant represents a real behavior the repo should detect.

- Derive the test from the selected task, cited design sections, and relevant acceptance criteria in `user-stories.md` — ground it in **expected behavior**, not in whatever the current code happens to do
- If the test fails against current code, that is a bug. Surface it instead of papering over it
- Add the narrowest unit test that kills the mutant, rerun `dotnet test`, then rerun Stryker to confirm the mutant is killed

### Disable the mutant inline with a reason (`equivalent`)

Choose this when the mutant is semantically indistinguishable from the original (for example `i <= n` vs `i < n + 1` on integers, or an initializer overwritten before the first read). Use Stryker's standard comment directive:

```csharp
// Stryker disable once <Mutator> : equivalent - <one-line reason>
problematicLine;
```

Stryker enforces the `:` reason syntax. The reason text is the audit trail. Prefer `disable once` over block `disable all`/`restore all` — it narrows the exclusion to the single line.

### Disable the mutant inline with a reason (`low-value`)

Choose this when the mutant is in code not worth defending with a dedicated test: logging text, cosmetic string formatting, diagnostic-only branches whose behavior does not affect routing, thread isolation, approval gating, queue serialization, or startup validation.

```csharp
// Stryker disable once <Mutator> : low-value - <one-line reason>
cosmeticCall;
```

Low-value must not be used to avoid writing a legitimate test. If the mutant is in routing, thread isolation, approval, queueing, or startup validation, low-value is almost never the right answer. Prefer a test.

### Exclude a whole type from mutation

For pure DTOs, marker classes, or types whose safety-critical behavior is tracked under a different task (T-012/T-015/T-016/T-023), apply `[ExcludeFromCodeCoverage]` at the type level and add an explanatory comment pointing at the task that owns the real coverage:

```csharp
using System.Diagnostics.CodeAnalysis;

// Covered by T-023 once startup validation mutation scope expands to Host.
[ExcludeFromCodeCoverage]
public sealed record FooConfiguration(...);
```

This is a bigger hammer than an inline disable. Use it only for types where per-line disables would accumulate into noise.

## Config-level noise suppression

Already applied in `stryker-config.json`:

- `ignore-methods`: `*Log`, `Console.Write*`, `*Exception.ctor` — skips mutations inside logging calls and exception-constructor arguments (noise, not behavior)

Adding to these should be a deliberate decision, not a reflex when a run fails. Prefer inline disables for one-off cases.

## What to report

In the final implementation summary, include:

- Which safety-critical projects ran the mutation check
- The mutation score per project (100% if the check passed)
- For every mutant that was disabled with a comment or excluded via attribute in this task, include: file path, line (or type), mutator, category (`equivalent` or `low-value`), and the exact reason text

## Failure modes

- Stryker itself fails to start (tool missing, solution mismatch, build error) → report as a blocker
- Score below `break: 100` → the check failed; do not commit. Either kill the survivor, disable-with-reason, or (if scope-appropriate) add `[ExcludeFromCodeCoverage]` with a task reference
- Stryker runs longer than an agent session can reasonably wait → narrow scope via `project` subpath or `mutate` globs and record the narrowing in the summary
- NuGet feed blocks the tool install → fix the manifest or use `--source`; do not silently skip
