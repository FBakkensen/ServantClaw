---
name: implement-next-task
description: >-
  Implement the next unblocked backlog task for the current project by reading
  `tasks.md`, selecting the first eligible `Status: [ ]` item, interviewing
  the user with the `grill-me` flow, presenting an approval checkpoint, and
  then implementing and verifying the task under the repo's engineering
  principles. Use when Codex should drive backlog execution for this repository
  instead of handling a one-off coding request.
---

# Implement Next Task

## Overview

Use this skill to execute the repository's next unblocked task with a repeatable workflow: select the task, read its cited sources, research any time-sensitive external details, interview the user to resolve the remaining decisions, present a compact approval checkpoint, then implement, verify, and finish the work cleanly.

Treat this as a delivery workflow, not a brainstorming template. Move the task forward end to end unless the user withholds approval or the task is genuinely blocked.

## Workflow

### 1. Select the task

Read `tasks.md` first.

Pick the first task whose status is `Status: [ ]`, whose dependencies are satisfied, and which is not blocked. Follow the operating rules already defined in `tasks.md`.

If the top task is blocked by a missing decision or external dependency, add a `Blocked by:` line only when that blocker is real and specific, then stop and surface it clearly.

If the task is too large to plan, implement, and verify safely in one pass, replace it with smaller tasks that preserve the same intent and priority before attempting implementation.

### 2. Read the cited sources before interviewing

Do not rely on the backlog entry alone.

For the selected task:

- Read the full task entry in `tasks.md`
- Read the cited sections from `design.md`, `prd.md`, and `user-stories.md`
- Inspect the current codebase or repo contents before asking the user anything that can be discovered locally

When a source line cites a section title rather than a file path, search the repo documents for that heading and read the relevant section.

Treat the task's cited sources as the contract for planning, testing, and implementation.

### 3. Research any external or time-sensitive technical decisions

Assume the agent's built-in knowledge may be outdated.

Before interviewing or implementing, identify the concrete technical facts the task depends on. Use current documentation via the `context7` tools whenever any material decision depends on behavior that is not fully established by the repository itself, including:

- Framework, runtime, library, or BCL behavior
- External APIs, SDKs, CLIs, or services
- Tooling, hosting, runtime, deployment, or platform behavior
- Version-specific features, defaults, limits, or compatibility rules
- Any technical fact that may plausibly have changed since training

Do not justify skipping research merely because the task is "repo-local." A repo-local code change may still depend on current .NET, Telegram, Codex, or other platform behavior.

You may skip `context7` research only when all material implementation decisions are already determined by:

- the repository's existing code and tests
- the task's cited local design and product documents
- stable language-level reasoning that does not depend on current versioned behavior

When skipping research, state the exact fact pattern that made the skip safe. Name which material decisions were already fixed locally and which external behaviors were not relied on.

When research is needed:

- Use the `context7` tools to resolve the relevant library and query the current documentation before making decisions
- Carry forward the material findings into the grill phase, approval checkpoint, implementation choices, and verification strategy
- Do not treat research as a box-checking step; it should actively shape the plan when it reveals constraints, updated APIs, or better-supported patterns
- Treat missing research on a material external dependency as a workflow failure, not an optional optimization

## Grill Phase

Start the task by using the `grill-me` approach.

Apply these rules exactly:

- Ask exactly one question at a time
- State the question plainly
- Give a recommended answer in 1-3 sentences
- Explain briefly why the question matters
- Wait for the user's response before asking the next question

Use the `grill-me` decision-tree style:

- Resolve the highest-leverage unknown first
- Prefer dependencies before dependent branches
- Keep the thread coherent
- Stop when the major branches are resolved or the remaining questions are low-risk implementation details

Use the codebase-first rule during the grill phase:

- Inspect the repo instead of asking when the answer should be discoverable from files, APIs, conventions, or existing implementation
- Summarize what you found, then continue to the next unresolved branch

Prioritize questions in this order when relevant:

1. Goal and success criteria
2. Constraints and non-goals
3. Interfaces and architectural boundaries
4. Data model or project structure
5. Failure modes and edge cases
6. Testing, observability, migration, and rollout concerns

The final step of the grill phase must be an approval request. Do not edit code before that approval.

## Approval Checkpoint

Before implementation, present a compact checkpoint that lets the user approve or redirect the work.

Follow the output contract in [references/checkpoint-contract.md](references/checkpoint-contract.md).

The checkpoint must include:

- The selected task ID, title, and dependency check
- The distilled requirements from the cited sources
- Important decisions already resolved during the interview
- Assumptions and open risks that still matter
- An overview of the test plan in Gherkin format
- The key flows shown as either a Mermaid diagram or a flat table
- A concise implementation plan tied to the expected files, projects, or layers when that can be inferred
- A direct approval request for implementation

If the user does not approve, stay in planning mode and revise the checkpoint instead of coding.

## Implementation Rules

Once approved, treat the engineering principles in `design.md` as mandatory execution rules.

### Mandatory engineering principles

- Prefer test-driven development when the behavior can be specified clearly
- Derive tests from the selected task, cited design sections, and relevant acceptance criteria in `user-stories.md`
- Keep business logic separate from framework and infrastructure code
- Preserve interface-driven boundaries at external or unstable seams
- Push side effects to the edges
- Design for deterministic tests around time, file IO, process supervision, and message delivery
- Treat tests, analyzers, formatting, and architecture rules as required guardrails

### Implementation sequence

1. Reconfirm the selected task and approved checkpoint
2. Inspect the existing code or repo structure that the change must fit into
3. Add or update tests first when the behavior is specifiable
4. Implement the smallest production change that satisfies the tests and task scope
5. Run the `dotnet-simplifier` skill after the implementation is in place and apply the in-scope simplifications that preserve the selected task's behavior
6. Run the task's required verification commands and any nearby targeted tests
7. Fix issues until the simplification pass and verification pass, or a real blocker is identified
8. Run the mutation-check protocol in [references/mutation-check-protocol.md](references/mutation-check-protocol.md) when the current diff touches a configured safety-critical project. The check is a hard gate: Stryker is configured with `break: 100`, so every mutant must be either killed by a test or explicitly disabled inline with a reason comment (or excluded via `[ExcludeFromCodeCoverage]` when the scope is owned by a different task). The check must pass before commit.
9. Review `AGENTS.md` just before commit time and decide whether the finished work introduced durable guidance future agents need
10. Update `AGENTS.md` only when there is something notable to preserve, such as design decisions, important implementation patterns, architectural constraints, operational caveats, or other long-lived guidance
11. Do not add transient process notes, execution logs, or backlog/progress tracking to `AGENTS.md`; that kind of state belongs in `tasks.md`
12. Create a focused commit only after the task is implemented, verified, any allowed `tasks.md` updates are in place, and any warranted `AGENTS.md` update has been made

Do not silently widen scope beyond the selected task unless that is required to satisfy the cited design contract.

## Verification And Completion

Use the task's `Verification` section as the minimum verification bar. Run the `dotnet-simplifier` skill as part of verification after implementation changes are ready for refinement. Add narrower targeted checks when they improve confidence.

When the changed files touch a configured safety-critical project, the mutation-check protocol in [references/mutation-check-protocol.md](references/mutation-check-protocol.md) is part of verification. The check is a hard gate at 100% mutation score: every mutant is either killed, disabled inline with a reason, or excluded via `[ExcludeFromCodeCoverage]` with a task reference.

For .NET tasks in this repository, expect verification to usually include some combination of:

- `dotnet build`
- `dotnet test`
- `dotnet format --verify-no-changes`

Do not mark the task complete until the `dotnet-simplifier` skill has been run, the required verification passes, or you have clearly reported why one of those steps could not be completed.

Do not treat the workflow as fully complete until the finished work has been committed, unless the user explicitly asks you not to commit or a real blocker prevents creating the commit.

## Updating `tasks.md`

You may update `tasks.md` only within its stated operating rules:

- Mark the selected task complete when implementation and verification are done
- Add a `Blocked by:` line when a real blocker prevents progress
- Replace an oversized task with smaller tasks that preserve intent and priority

Do not reorder tasks, rewrite unrelated tasks, or change backlog policy.

## Updating `AGENTS.md`

Review `AGENTS.md` after implementation and verification, just before creating the commit.

Update it only when the completed work adds durable repository guidance that will help future agents, for example:

- Important design decisions
- Significant implementation patterns or boundaries
- Architectural constraints
- Operational caveats
- New long-lived conventions

Do not add:

- Step-by-step process notes
- Temporary debugging details
- Execution logs or status updates
- Backlog, task-tracking, or progress information that belongs in `tasks.md`

## Final Response

When the work is complete, report:

- What was implemented
- Whether external documentation research was required
- If research was required, what sources were consulted and which decisions they informed
- If research was skipped, which material decisions were already fixed by local code or docs and which external behaviors were not relied on
- Whether the `dotnet-simplifier` skill was run and the result
- What was verified and the result
- Whether `tasks.md` was updated
- Whether `AGENTS.md` was reviewed and whether it was updated
- Whether the changes were committed and the commit result
- Whether the mutation-check ran, its per-project score, and for every mutant disabled inline or via attribute during this task: file path, line or type, mutator, category (equivalent or low-value), and the exact reason text
- Any remaining risks, follow-up work, or blockers

If implementation was not approved or could not be completed, say exactly where execution stopped and what decision or blocker remains.
