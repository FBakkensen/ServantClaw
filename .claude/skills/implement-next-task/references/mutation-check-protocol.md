# Mutation Check Protocol

Governs how `/implement-next-task` runs mutation testing on safety-critical code and how to handle surviving mutants. Advisory: the task can complete even when mutants survive, as long as each surviving mutant is surfaced and categorized.

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
3. Wait for completion. HTML report lands in `StrykerOutput/<timestamp>/reports/mutation-report.html`; a cleartext summary is printed.

If the current branch has no `main` parent (detached HEAD, unusual rebase state), fall back to `--since:HEAD~1`, or skip the check with an explicit note. Do not treat `since` resolution errors as a blocker.

## How to interpret surviving mutants

For each surviving mutant, pick exactly one of three outcomes and record it in the final implementation summary.

### Add test

Default outcome. Choose this when the mutant represents a real behavior the repo should detect. Add the narrowest unit test that kills the mutant, rerun `dotnet test`, then rerun Stryker for that project to confirm the mutant is killed.

### Equivalent

Choose this when the mutant is semantically indistinguishable from the original (e.g. `i <= n` vs `i < n + 1` on integers, or a mutated initializer overwritten before the first read). Record:

- The mutant's file, line, and operator
- Why the mutant produces identical observable behavior
- Why no test could distinguish it

Equivalent mutants must be called out, not silently accepted.

### Low-value

Choose this when the mutant is in code not worth defending with a dedicated test: logging text, cosmetic string formatting, diagnostic-only branches whose behavior does not affect routing, thread isolation, approval gating, queue serialization, or startup validation. Record:

- The mutant's file, line, and operator
- Which safety-critical invariant is demonstrably unaffected
- Why the cost of a test exceeds the value

Low-value must not be used to avoid writing a legitimate test. If the mutant is in routing, thread isolation, approval, queueing, or startup validation, low-value is almost never the right answer.

## What to report

In the final implementation summary, include:

- Which safety-critical projects ran the mutation check
- The mutation score per project (or `N/A` when `--since` reported no changes in that project)
- Per surviving mutant: file path, line, mutator, and category (add test / equivalent / low-value) with a one-line justification
- Confirmation that any "add test" outcomes actually killed the mutant on re-run

## Failure modes

- Stryker itself fails to start (tool missing, solution mismatch, build error) -> report as a blocker, not a passing advisory check
- Stryker runs longer than an agent session can reasonably wait -> narrow scope via `project` subpath or `mutate` globs and record the narrowing in the summary
- NuGet feed blocks the tool install -> fix the manifest or use `--source`; do not silently skip
