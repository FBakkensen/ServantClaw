# ServantClaw v1 Design

## Purpose

This document translates the product requirements in `prd.md` and `user-stories.md` into a concrete v1 system design for ServantClaw.

ServantClaw v1 is a single-user Telegram bot hosted as a Windows Service. It acts as a remote front end to a local Codex-backed assistant running through `codex app-server` over `stdio` JSON-RPC. The design optimizes for operational simplicity, explicit routing, durable local state, and auditable approval handling.

## Design Goals

- Keep v1 operationally simple and trustworthy
- Preserve strict isolation across agent, project, and Telegram chat context
- Run unattended as a Windows Service without requiring an interactive desktop session
- Support one long-lived shared Codex backend process
- Keep all persistent state local and file-based
- Allow remote use from Telegram while keeping risky and maintenance actions explicitly gated
- Leave clean seams for future transport, retrieval, and multi-user expansion

## Engineering Principles

ServantClaw should be built with strong engineering discipline because it is intended to be maintained and extended by humans and agents over time. The codebase should optimize for safe change, fast feedback, and clear boundaries rather than clever shortcuts.

### Test-driven development

- Prefer test-driven development for new behavior, especially routing, approvals, queueing, persistence, and backend supervision logic
- Write a failing test before implementing a behavior when the behavior can be specified clearly
- Treat acceptance criteria from `user-stories.md` as the source for high-value behavior tests
- When a bug is found, add a regression test before or with the fix
- Default test stack: `xUnit` for tests and `FluentAssertions` for readable assertions

### Clean and testable code

- Keep business logic separate from framework and infrastructure code
- Prefer small focused classes with explicit responsibilities
- Push side effects to the edges so core orchestration and state transitions can be tested without real Telegram or backend processes
- Avoid hidden global state, static singletons for mutable behavior, and tightly coupled helpers
- Design for deterministic tests around time, file IO, process supervision, and message delivery

### Interface-driven boundaries

- Use interfaces at external and architectural boundaries, not as decoration on every class
- All unstable or side-effecting dependencies should be behind interfaces, including:
  - Telegram transport
  - Codex backend client
  - state store
  - clock and ID generation
  - process launcher and health supervision
  - logging or event publishing seams where behavior matters to tests
- Core use cases should depend on abstractions and plain domain models rather than concrete infrastructure implementations

### London-school unit testing for orchestration

- Use London-school testing for orchestration-heavy components where behavior is defined by collaboration between objects
- Verify important interaction contracts at boundaries such as:
  - command handlers calling the correct use cases
  - queue manager sequencing turns correctly
  - approval coordinator persisting and resuming the right flow
- backend supervisor triggering restart and error reporting
- Do not let mock-heavy tests replace all state-based tests; use them where collaboration behavior is the thing being specified
- Default mocking library: `NSubstitute`

### State-based and integration testing

- Use state-based tests for pure domain logic and state transitions
- Add integration tests for the most important real seams, especially:
  - JSON persistence behavior
  - config loading and startup validation
  - Telegram adapter parsing and command routing
  - `codex app-server` transport contract once protocol details are confirmed
- Prefer thin end-to-end slices for critical workflows over broad fragile test pyramids
- Keep integration tests in a separate test project so unit-test feedback stays fast
- Use temporary filesystem fixtures and process test doubles where possible before relying on live external dependencies

### Mutation testing

- Use mutation testing to validate that core tests actually detect behavioral change
- Prioritize mutation testing for the most safety-critical areas:
  - project routing
  - thread isolation
  - approval gating
  - queue serialization
  - startup validation
- Mutation score is a quality signal, not a vanity metric, but core safety logic should have strong mutation resistance
- Preferred tool: `Stryker.NET`

### Linters and structural verification

- The repository should enforce formatting, static analysis, and architectural rules in automated checks
- Use .NET analyzers and formatting enforcement as a baseline
- Add structural verification so forbidden dependencies and layer violations fail fast
- Examples of structural rules worth enforcing:
  - orchestration code must not reach around interfaces into transport details
  - domain logic must not depend directly on Telegram SDK types
  - state and approval logic must not bypass the persistence abstraction
  - infrastructure implementations must not be referenced from unrelated domain layers
- Baseline tooling:
  - `dotnet format` for formatting and analyzer enforcement
  - built-in Roslyn analyzers enabled at a strict level
  - nullable reference types enabled across the solution
  - warnings treated as errors in CI for production projects
- Structural verification options:
  - architecture tests with `NetArchTest.Rules`
  - optional custom Roslyn analyzers later if simple architecture tests are not enough

### Agent-friendly development flow

- The codebase should be easy for an agent to inspect, change, and verify safely
- Favor explicit names, stable module boundaries, and predictable file layout
- Keep tests fast enough that agents can run them frequently during implementation
- Treat linters, analyzers, and tests as mandatory guardrails rather than optional polish
- Prefer making the correct path easy to follow over relying on tribal knowledge
- Standard local verification command set should stay small and memorable:
  - `dotnet test`
  - `dotnet format --verify-no-changes`
  - mutation test command for targeted safety-critical projects

### Definition of done

A change is not done when it merely works locally. For ServantClaw, a meaningful change should normally include:

- automated tests covering the behavior
- passing linters and analyzers
- no architectural rule violations
- updated docs or design notes when the change alters a design contract
- clear observability for failures in service-critical paths

## Key Decisions

### Runtime and hosting

- ServantClaw v1 is implemented as a `.NET` Worker Service
- The Worker Service is installed and run as a native Windows Service
- The service owns one long-lived shared `codex app-server` child process
- Telegram integration uses long polling in v1

### Context and routing

- The system exposes two logical agents: `general` and `coding`
- A project must be actively selected before normal task execution
- Physical projects live in one shared project catalog under the bot root
- Bindings are per `(agent, telegram chat)` and reference shared project IDs
- The durable conversation unit is one Codex thread per `(agent, project, telegram chat)` triple
- Turns are serialized one at a time per `(agent, project, telegram chat)` context

### State and approvals

- Persistent machine-managed state is stored as JSON files
- Pending approvals are persisted and restart-safe
- Normal risky actions use a single approval step
- Environment-changing maintenance actions also use a single approval step, but with stronger warnings, richer summaries, and a distinct approval type for auditability

### External dependencies

- Telegram bot authentication is provided through a local config file with a placeholder token value by default
- If the Telegram token is missing or invalid, startup fails clearly and writes guidance to the durable service log
- Codex authentication is a separate prerequisite and must already be provisioned locally before service startup
- Online retrieval is provided by the configured Codex environment rather than by a separate v1 service-owned web integration

### Response model

- Telegram messages are not streamed token-by-token in v1
- The bot sends one final reply per turn
- The bot may send a short interim status message such as "working" for long-running turns

## High-Level Architecture

The system is organized into the following layers:

1. Telegram adapter
2. Bot orchestration layer
3. Routing and context state layer
4. Turn executor and per-context queue manager
5. Approval coordinator
6. Codex backend abstraction
7. `stdio` transport adapter for `codex app-server`
8. File-based state store
9. Windows Service host and health supervision

### Component responsibilities

#### 1. Windows Service host

Owns process startup, shutdown, service lifecycle integration, and durable runtime logging.

#### 2. Telegram adapter

Handles long polling, incoming updates, command parsing, outbound message delivery, and owner filtering.

#### 3. Bot orchestration layer

Coordinates command handling, normal user-message routing, approval replies, status responses, and operator-visible errors.

#### 4. Routing and context state layer

Determines the active `(agent, project, telegram chat)` context, enforces explicit project binding, and resolves the thread mapping for each turn.

#### 5. Turn executor and queue manager

Maintains one in-flight turn at a time per context. Different contexts may run independently, but a single context is strictly serialized.

#### 6. Approval coordinator

Creates pending approval records, sends Telegram approval requests, tracks approval lifecycle, and resumes or terminates blocked work after `/approve` or `/deny`.

#### 7. Codex backend abstraction

Presents a stable internal interface for:

- ensure backend available
- start or resume thread
- send user turn
- receive completion or approval request
- report backend health

This interface isolates the rest of the bot from backend transport details.

#### 8. `stdio` transport adapter

Launches `codex app-server` as a child process and communicates over redirected stdin/stdout using JSON-RPC. This is the only transport implemented in v1.

#### 9. File-based state store

Persists config, owner identity, project catalog metadata, chat bindings, thread mappings, approvals, and logs under the bot root using JSON files and append-oriented logs.

## Process Model

### Shared backend process

ServantClaw runs one shared long-lived `codex app-server` process for the whole service. Isolation is enforced by ServantClaw routing and durable thread mapping rather than by running one backend per agent or project.

This choice keeps v1 simpler in:

- process supervision
- memory and CPU usage
- restart handling
- service installation and troubleshooting

### Backend recovery

If `codex app-server` crashes or becomes unhealthy, ServantClaw attempts automatic restart. Persisted thread mappings and approval records remain the source of truth when they are healthy. If an active user request is affected, the bot reports the interruption clearly in Telegram and in logs.

If normal persisted routing state is unavailable because it has been quarantined as corrupted, the system must still preserve a clean fallback path that can start a fresh Codex conversation for recovery purposes using startup configuration and Telegram ownership checks rather than the damaged records.

## Telegram Interaction Model

### Long polling

The service uses Telegram long polling rather than webhooks. This avoids public HTTPS exposure, certificate management, and reverse-proxy requirements for v1.

### Owner-only access control

Only one approved Telegram user may operate the bot in v1.

- Messages from the approved owner are processed normally
- Messages from other users are ignored or rejected safely
- Unauthorized responses must not leak sensitive internal details

The approved owner is provisioned explicitly through local configuration or bootstrap state, not by auto-claiming the first inbound user.

### Commands

Required commands:

- `/agent <agent-id>`
- `/project <agent-id> <project-id>`
- `/clear`
- `/approve <id>`
- `/deny <id>`

Nice-to-have commands included in the design:

- `/projects`
- `/status`

### Non-command messages

For a normal user message:

1. Validate sender is the approved owner
2. Resolve active agent for the Telegram chat
3. Require an active project binding for that agent and chat
4. Resolve the thread for `(agent, project, chat)`
5. Enqueue the turn for that context
6. Execute the turn through the shared backend
7. Return one final Telegram reply or an approval request

If no active project is bound, ServantClaw does not run the turn. It instead asks the user to bind or confirm a project.

## Routing and Isolation Model

### Agent model

The bot exposes two logical agents:

- `general`
- `coding`

Agent state is isolated in terms of:

- active chat binding
- thread mappings
- approval context
- logs and audit records

The agents share the same physical project catalog, but they do not share active bindings or threads.

### Project model

Projects are local folders under a shared root, conceptually:

```text
bot-root/
  projects/
    <project-id>/
```

Bindings are maintained per `(agent, telegram chat)` pair. This avoids duplicating the same project tree under separate `general/` and `coding/` hierarchies while preserving agent isolation where it matters.

### Thread model

Each `(agent, project, telegram chat)` triple maps to exactly one current Codex thread reference. `/clear` rotates that mapping to a new thread without deleting historical references.

### Ambiguity handling

ServantClaw must never silently guess a project in ambiguous situations.

- If there is no active project, the bot requires explicit selection
- If future intent-based suggestion logic is added, it may propose candidates
- Routing happens only after explicit user confirmation

## Queueing and Turn Execution

Each `(agent, project, telegram chat)` context has a strict single-consumer queue.

Rules:

- Only one in-flight turn per context
- Different contexts may execute concurrently
- Messages in the same context execute in arrival order
- Approval pauses suspend the active turn for that context
- While a turn is paused for approval, later messages for that context remain queued or are rejected with a clear explanation, depending on final implementation preference

This prevents race conditions around:

- thread history ordering
- streamed backend events
- approval pauses
- `/clear` behavior
- reply delivery

## Approval Model

### Approval lifecycle

When the backend requests approval for a risky action:

1. ServantClaw creates a durable pending approval record
2. The active turn enters a blocked state
3. The bot sends a Telegram message containing:
   - approval ID
   - short human-readable summary
   - approval type
4. The owner responds with `/approve <id>` or `/deny <id>`
5. ServantClaw updates the approval record durably
6. The blocked turn resumes or terminates accordingly

### Approval classes

V1 uses two approval classes:

#### Standard risky action

Used for normal tool or action approvals requested during assistant execution.

#### Maintenance action

Used for environment-changing operations such as:

- changing MCP configuration
- adding, updating, or removing skills or local agent assets
- upgrading `codex-rs` or `codex app-server`
- reloading or restarting Codex-side services

Maintenance approvals remain single-step in v1, but they are stricter by:

- carrying a more explicit warning
- including a richer human-readable summary
- using a distinct persisted type for audit and reporting

### Durability and restart safety

Approval state is persisted to disk before the Telegram approval request is sent. On service restart, pending approvals are reloaded so the system can safely reject stale resumes, continue waiting, or recover blocked state consistently.

## Codex Backend Integration

### Abstraction boundary

The rest of ServantClaw talks to a backend interface rather than directly to JSON-RPC transport code. This protects bot logic from future transport changes.

Conceptual operations:

- `EnsureBackendReady()`
- `CreateThread()`
- `ResumeThread(threadRef)`
- `SendTurn(threadRef, message, context)`
- `ContinueApprovedAction(approvalRef, decision)`
- `GetBackendHealth()`

### `stdio` JSON-RPC transport

The transport adapter is responsible for:

- spawning `codex app-server`
- redirecting stdin, stdout, and stderr
- sending JSON-RPC requests
- correlating responses and events
- surfacing approval requests to the approval coordinator
- detecting backend death, startup failure, or protocol failure

### Authentication prerequisite

ServantClaw does not perform interactive Codex sign-in via Telegram in v1. Codex authentication must already be provisioned on the host machine before service startup.

Startup validation should detect the likely absence of Codex auth and fail clearly rather than leaving the service in a partially available state.

## Remote Self-Management

Remote self-management remains inside the shared assistant session rather than being split into a separate maintenance mode or separate backend.

This means the user can request environment changes conversationally through the normal bot, but the system still applies extra control through the maintenance approval class and stronger audit records.

Supported categories in v1 design:

- MCP server configuration changes
- skill or local asset updates
- backend runtime version inspection
- backend runtime upgrade initiation
- backend reload or restart when required

### Upgrade handling expectations

For backend runtime updates, the system should aim to report:

- current installed version
- requested target version if known
- whether update succeeded
- whether restart succeeded
- what version is running after the action
- what manual recovery is needed if the action fails

Exact upgrade mechanics may evolve during implementation, but these reporting expectations should remain part of the design contract.

## Online Information Access

V1 relies on the configured Codex environment to provide web and online retrieval capability. ServantClaw does not implement its own separate retrieval subsystem in v1.

Design consequences:

- retrieval capability is treated as an environment dependency
- startup or health checks should indicate whether this capability appears available
- retrieval-related failures should be reported clearly to the user
- the architecture still keeps an internal boundary so a future dedicated retrieval integration can be added without rewriting Telegram routing and approvals

## Persistence Design

### Storage principles

- All persistent state lives under one local bot root
- Persistent state is file-based and JSON-backed
- Files should be human-inspectable where practical
- Writes should be durable and designed to minimize corruption risk
- Historical references should be preserved where required, especially for thread rotation and approval audit
- Corrupted state must preserve enough evidence and diagnostics to support a later Codex-assisted recovery flow

### Conceptual layout

```text
bot-root/
  config/
    service.json
    telegram.json
    owner.json
  projects/
    <project-id>/
  state/
    chats/
      <chat-id>.json
    threads/
      <agent>/
        <chat-id>/
          <project-id>.json
    approvals/
      pending/
      resolved/
    quarantine/
      chats/
      threads/
      approvals/
    recovery/
      incidents/
  logs/
    service/
    telegram/
    backend/
    approvals/
```

### Suggested persisted records

#### Telegram config

- bot token placeholder by default
- polling configuration

#### Owner binding

- approved Telegram user ID
- optional username metadata for diagnostics

#### Chat state

- active agent for the chat
- per-agent active project binding

#### Thread state

- current thread reference for `(agent, project, chat)`
- prior thread references for `/clear` history preservation

#### Approval record

- approval ID
- approval class
- chat ID
- agent ID
- project ID
- thread reference
- human-readable summary
- creation time
- resolution time
- decision
- related operation metadata

### Corruption handling and recovery substrate

If a machine-managed state file is missing or malformed, ServantClaw must fail closed for the affected record rather than continue with guessed or partially trusted state.

The persistence layer should:

- quarantine the unreadable payload instead of overwriting it in place
- write machine-readable recovery diagnostics that identify the record type, canonical path, failure reason, and incident time
- preserve enough artifact data for a later Codex-assisted repair workflow
- allow unaffected chats, threads, and approvals to continue operating normally

This substrate does not itself perform the operator-facing repair conversation. It exists so later Telegram and Codex flows can recover the system without requiring direct filesystem edits by the operator.

## Configuration Model

### Human-edited startup config

ServantClaw should provide a human-edited config file with explicit placeholders, especially for the Telegram bot token.

Example expectations:

- placeholder value makes the missing token obvious
- startup validation rejects placeholder or malformed values
- startup failure writes clear remediation guidance to the durable service log

### Config categories

- Telegram bot token and polling settings
- bot root path
- backend executable path or discovery settings
- logging settings
- optional health and timeout settings

## Observability

V1 observability must support remote troubleshooting and local post-mortem inspection.

Minimum logging:

- Windows Service startup and shutdown
- Telegram long polling start, stop, and failures
- inbound update acceptance or rejection
- command handling
- backend launch, exit, restart, and connectivity failures
- approval creation and resolution
- per-turn success and failure
- configuration and startup validation failures

### Service-critical failure reporting

Durable service logs should be used for service-critical startup and runtime failures, especially:

- missing or invalid Telegram token
- backend startup failure
- missing required owner binding
- likely missing Codex authentication prerequisite

These messages should include clear guidance about the next operator action and remain available for post-mortem inspection under the bot root logging directory.

## Failure Handling

### Startup failures

The service should fail fast and clearly when critical prerequisites are missing, including:

- Telegram token missing or placeholder value still present
- Telegram token invalid
- approved owner not configured
- backend executable missing or not launchable
- likely missing Codex authentication dependency

### Runtime failures

During normal operation:

- unauthorized Telegram senders are rejected safely
- backend unavailability is surfaced clearly to the user
- backend process death triggers automatic restart
- corrupted or missing state files fail closed rather than misrouting work into the wrong context
- corrupted state creates recovery artifacts instead of forcing manual JSON repair as the only viable path
- approval IDs that are unknown, expired, or already resolved are rejected safely

## Security and Safety Boundaries

### Hard guarantees

- only one approved Telegram user can operate the bot
- the bot never silently routes ambiguous work to a guessed project
- `/clear` does not delete historical thread records
- risky actions require approval
- maintenance actions receive stricter approval classification and stronger audit visibility
- state isolation is enforced by `(agent, project, chat)` routing and durable thread mapping

### Explicit non-goals for v1

- multi-user access control
- multiple active Telegram bots in one process
- database-backed persistence
- webhook hosting
- concurrent turns in one context
- destructive history deletion
- separate service-owned web retrieval subsystem

## Open Risks

### Codex protocol details

The design assumes `codex app-server` can be supervised from .NET over `stdio` JSON-RPC with the needed operations for thread lifecycle, turn submission, and approval handling. This is viable in principle, but the exact protocol contract still needs validation against the real backend.

### Headless Codex auth dependency

Codex authentication is treated as pre-provisioned. If the actual runtime has brittle credential behavior under Windows Service hosting, additional bootstrap or service-account constraints may be needed.

### Shared-backend isolation limits

V1 relies on routing and thread isolation rather than process isolation. If future testing shows cross-thread behavior or resource contention that weakens trust, a later version may need stronger process boundaries.

### Remote maintenance sharp edges

Keeping maintenance inside the normal assistant session is simpler for the operator but leaves a narrower safety margin than a dedicated workflow. The maintenance approval class and audit trail must therefore be implemented carefully.

### File-state robustness

File-based JSON state is appropriate for v1, but it requires disciplined atomic-write patterns and corruption handling to avoid partial-write problems after crashes.

The harder product requirement is remote recoverability: the operator may be on Telegram with no direct access to the machine, so corruption handling must preserve a Codex-reachable self-repair path rather than devolve into "fix the JSON manually on disk."

## Implementation Sequence

1. Define the solution structure, project boundaries, JSON schemas, and core interfaces
2. Set up engineering guardrails first:
   - `xUnit`
   - `FluentAssertions`
   - `NSubstitute`
   - `dotnet format`
   - Roslyn analyzers
   - nullable reference types
   - architecture tests with `NetArchTest.Rules`
3. Build the `.NET` Worker Service host and Windows Service integration
4. Implement Telegram long polling and owner filtering
5. Implement chat state, project binding, and thread mapping persistence
6. Implement per-context queueing and command handling
7. Integrate the shared `codex app-server` process behind a backend abstraction
8. Implement approval persistence and Telegram approval commands
9. Add `/clear`, `/status`, and `/projects`
10. Add backend supervision, restart handling, and startup validation
11. Add maintenance approval classification and audit details
12. Add mutation testing with `Stryker.NET` for the most safety-critical components
13. Validate online retrieval dependency behavior through the Codex environment

## Summary

ServantClaw v1 is designed as a local-first `.NET` Windows Service with one shared `codex app-server`, Telegram long polling, explicit project binding, strict per-context queueing, durable JSON state, and restart-safe approval handling. The architecture keeps v1 small while preserving clear seams for future transport changes, richer online retrieval, and broader deployment models.
