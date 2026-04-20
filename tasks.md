# Tasks

This file is the implementation backlog for ServantClaw v1.

Operating rules:
- Tasks are ordered strictly by priority from top to bottom.
- Codex should default to the first task with `Status: [ ]` whose dependencies are satisfied and which is not blocked.
- If the top task is blocked by a missing decision or external dependency, Codex should add a `Blocked by:` line to that task and ask the user before proceeding.
- Codex may update this file during execution only to mark completed tasks, add blocking notes, or split an oversized task into smaller replacement tasks.
- Task status is binary only: `Status: [ ]` or `Status: [x]`.
- Task IDs are stable and never renumbered.
- If a task is too large to plan, implement, and verify safely in one pass, Codex should replace it with smaller tasks that preserve the same intent and priority.
- `Verification` describes how completion will be proven; verification results are reported in chat, not logged here.

## T-001 - Scaffold the .NET solution and project structure
Status: [x]
Goal: Create the initial solution, production projects, and test projects with boundaries that match the v1 architecture.

Scope:
- Create the .NET Worker Service entry project
- Create production projects for core application boundaries
- Create unit and integration test projects
- Add project references that enforce intended dependency direction

Source:
- Design - Implementation Sequence 1
- Design - High-Level Architecture

Definition of Done:
- The repository contains a buildable solution with named projects aligned to the architecture
- Test projects exist and reference the appropriate production projects
- The solution builds successfully
- Project boundaries are clear enough to support isolated testing and later architecture rules

Verification:
- Run `dotnet build`
- Confirm the solution contains the worker, core application code, and test projects

## T-002 - Add engineering guardrails and baseline quality configuration
Status: [x]
Goal: Establish the default development and verification tooling expected by the design before feature work begins.

Scope:
- Add `xUnit`, `FluentAssertions`, and `NSubstitute`
- Enable nullable reference types
- Configure analyzers and treat warnings as errors for production projects where practical
- Add formatting configuration and architecture test baseline

Depends on:
- T-001

Source:
- Design - Engineering Principles
- Design - Implementation Sequence 2

Definition of Done:
- Test and analyzer dependencies are configured in the solution
- Formatting and analyzer settings are committed in repo configuration
- Architecture test project baseline exists
- The solution builds and tests run with the new quality settings enabled

Verification:
- Run `dotnet build`
- Run `dotnet test`
- Run `dotnet format --verify-no-changes`

## T-003 - Define the core domain contracts and state models
Status: [ ]
Goal: Introduce the core models and interfaces for chat context, routing, threads, approvals, configuration, and backend interaction.

Scope:
- Define domain models for agents, chat bindings, thread mappings, approvals, and service configuration
- Define interfaces for state store, backend client, clock, ID generation, and process supervision
- Keep infrastructure concerns out of the domain contracts

Depends on:
- T-001
- T-002

Source:
- Design - Interface-driven boundaries
- Design - Persistence Design
- Design - Codex Backend Integration

Definition of Done:
- Core models and interfaces exist in the solution
- Contracts are sufficient to support upcoming routing, persistence, and backend tasks
- Unit tests cover key invariants in the pure domain types where applicable

Verification:
- Run `dotnet test`
- Review the contracts to confirm Telegram and process-specific types do not leak into domain models

## T-004 - Implement JSON-backed configuration loading and startup validation
Status: [ ]
Goal: Load required configuration from disk and fail fast when critical prerequisites are missing or invalid.

Scope:
- Add JSON-backed loading for service and Telegram configuration
- Validate missing or placeholder bot token, missing owner binding, and missing backend executable settings
- Surface validation failures in a form the service host can report clearly

Depends on:
- T-003

Source:
- PRD - Storage Requirements
- Design - Configuration Model
- Design - Failure Handling

Definition of Done:
- Configuration files can be loaded from the expected locations
- Invalid startup configuration fails before normal runtime begins
- Validation errors are explicit and suitable for logs and operator troubleshooting
- Tests cover valid and invalid startup configuration scenarios

Verification:
- Run `dotnet test`
- Run the service with missing or placeholder config and confirm startup fails clearly

## T-005 - Build the Worker Service host and Windows Service lifecycle integration
Status: [ ]
Goal: Create the service host that starts cleanly, shuts down cleanly, and wires together the application runtime.

Scope:
- Implement the Worker Service startup path
- Add Windows Service hosting integration
- Wire dependency injection and application startup flow
- Ensure graceful shutdown behavior for hosted components

Depends on:
- T-004

Source:
- PRD - Service Hosting Requirements
- Design - Runtime and hosting
- Design - Implementation Sequence 3

Definition of Done:
- The app can run as a worker locally and is structured for Windows Service hosting
- Startup invokes configuration validation before runtime services begin
- Shutdown path stops hosted components cleanly
- Tests cover startup and shutdown orchestration where feasible

Verification:
- Run `dotnet build`
- Run the host locally and confirm clean startup and shutdown behavior

## T-006 - Add durable logging and service-critical event reporting
Status: [ ]
Goal: Provide the minimum observability required to operate and troubleshoot ServantClaw as a service.

Scope:
- Configure durable application logging
- Add structured logs for startup, shutdown, command handling, approvals, and backend lifecycle
- Add Windows Event Log reporting for service-critical failures where supported

Depends on:
- T-005

Source:
- PRD - Observability Requirements
- Design - Observability

Definition of Done:
- Logging is configured for the main service flows
- Service-critical startup failures are emitted clearly
- Tests verify important logging and reporting behavior where practical

Verification:
- Run the host through a success path and a startup failure path and inspect emitted logs

## T-007 - Implement the file-based state store with atomic JSON persistence
Status: [ ]
Goal: Persist machine-managed state safely under the bot root using human-inspectable JSON files.

Scope:
- Implement persistence for chat state, thread mappings, approvals, and owner/config metadata as needed
- Use atomic write patterns to reduce corruption risk
- Add repository-safe file layout matching the design

Depends on:
- T-003
- T-004

Source:
- PRD - Storage Requirements
- Design - Persistence Design

Definition of Done:
- The state store can read and write the required records under the expected directory structure
- Write behavior is resilient against partial-update risks as far as practical in v1
- Integration tests cover JSON persistence and reload behavior

Verification:
- Run `dotnet test`
- Inspect generated state files for expected structure and contents

## T-008 - Implement owner filtering and Telegram update intake
Status: [ ]
Goal: Receive Telegram updates, accept only the configured owner, and reject or ignore unauthorized users safely.

Scope:
- Integrate Telegram long polling
- Parse incoming updates into application commands and messages
- Enforce owner-only access at the adapter boundary

Depends on:
- T-005
- T-006
- T-007

Source:
- PRD - Single-User Access Control
- Design - Telegram adapter
- Design - Implementation Sequence 4

Definition of Done:
- The service can receive Telegram updates from the configured bot
- Approved owner messages reach application handling
- Unauthorized users are ignored or rejected safely without leaking sensitive details
- Tests cover owner filtering and update parsing behavior

Verification:
- Run `dotnet test`
- Exercise the bot with owner and non-owner messages in a controlled environment

## T-009 - Implement chat agent selection and project binding state
Status: [ ]
Goal: Persist and resolve the active agent and per-agent project binding for each Telegram chat.

Scope:
- Implement `/agent <agent-id>`
- Implement `/project <agent-id> <project-id>`
- Persist active agent and per-agent project bindings per chat

Depends on:
- T-007
- T-008

Source:
- US-03
- US-04
- Design - Routing and Isolation Model

Definition of Done:
- Agent switching works for `general` and `coding`
- Project binding works per `(agent, chat)` pair
- Invalid agent IDs and project IDs are rejected clearly
- Tests cover persistence and isolation rules for chat state

Verification:
- Run `dotnet test`
- Send `/agent` and `/project` commands and confirm persisted state and user-facing confirmations

## T-010 - Implement the project catalog and explicit project resolution rules
Status: [ ]
Goal: Provide project discovery and the safety checks that prevent ambiguous or implicit routing.

Scope:
- Implement project catalog discovery from the bot root
- Validate project existence during project binding
- Enforce explicit project selection before normal turn execution

Depends on:
- T-007
- T-009

Source:
- PRD - Projects
- US-05
- Design - Ambiguity handling

Definition of Done:
- Project IDs are resolved from the configured local project catalog
- Unknown projects are rejected clearly
- Normal turns do not execute when no active project is bound
- Tests cover missing project and no-active-project routing cases

Verification:
- Run `dotnet test`
- Attempt normal messages without project selection and confirm safe refusal

## T-011 - Implement thread mapping persistence and `/clear` rotation
Status: [ ]
Goal: Maintain one current Codex thread per `(agent, project, chat)` and allow safe thread reset without deleting history.

Scope:
- Create and persist thread references by `(agent, project, chat)`
- Resume existing thread references for repeated work in the same context
- Implement `/clear` to rotate the current thread reference while preserving prior references

Depends on:
- T-007
- T-009
- T-010

Source:
- US-06
- US-08
- Design - Thread model

Definition of Done:
- Thread mappings persist across restarts
- Different agents, projects, and chats do not share thread references
- `/clear` creates a new current thread reference without deleting historical references
- Tests cover isolation and rotation behavior

Verification:
- Run `dotnet test`
- Execute repeated requests in multiple contexts and confirm separate persisted thread mappings

## T-012 - Build the per-context queue manager and turn execution contract
Status: [ ]
Goal: Serialize work one turn at a time per `(agent, project, chat)` context while allowing independent contexts to proceed separately.

Scope:
- Implement per-context queueing
- Ensure only one in-flight turn per context
- Define how queued messages behave when a turn is running or blocked for approval

Depends on:
- T-011

Source:
- Design - Queueing and Turn Execution
- Design - Implementation Sequence 6

Definition of Done:
- Turns are serialized per context
- Different contexts can execute independently
- Queue behavior is deterministic and covered by tests
- Blocking semantics are clear enough to support approval pauses

Verification:
- Run `dotnet test`
- Simulate concurrent messages across shared and separate contexts and confirm ordering rules

## T-013 - Implement the Codex backend abstraction and process supervisor
Status: [ ]
Goal: Introduce the internal backend interface and supervise one long-lived shared `codex app-server` process.

Scope:
- Implement backend readiness and health abstractions
- Launch and monitor a single shared backend process
- Detect startup failure, crash, and unhealthy states

Depends on:
- T-003
- T-005
- T-006

Source:
- PRD - Codex Backend
- Design - Codex Backend abstraction
- Design - Shared backend process

Definition of Done:
- The rest of the application can depend on a backend interface instead of process details
- The backend process can be started, monitored, and restarted through a supervisor
- Failures are surfaced clearly to the host and logs
- Tests cover supervisor behavior with process doubles where feasible

Verification:
- Run `dotnet test`
- Exercise backend supervisor startup and restart paths in a controlled environment

## T-014 - Implement the `stdio` JSON-RPC transport adapter for `codex app-server`
Status: [ ]
Goal: Communicate with the local Codex backend over redirected standard IO behind the backend abstraction.

Scope:
- Spawn `codex app-server` with redirected stdin/stdout/stderr
- Send and receive JSON-RPC messages
- Correlate requests, responses, completions, and approval events

Depends on:
- T-013

Source:
- PRD - Codex Backend
- Design - `stdio` JSON-RPC transport
- Design - Open Risks - Codex protocol details

Definition of Done:
- The transport adapter can send supported requests and receive corresponding responses/events
- Transport errors are reported clearly and do not corrupt application state
- Tests cover protocol handling as far as the known contract allows

Verification:
- Run `dotnet test`
- Exercise a controlled integration path against a real or representative backend process

## T-015 - Wire end-to-end Telegram message execution through the backend
Status: [ ]
Goal: Route a normal Telegram message from the approved user through context resolution, queueing, backend execution, and final reply delivery.

Scope:
- Resolve active context from chat state
- Ensure backend readiness and thread selection
- Send the turn to the backend and deliver one final response back to Telegram
- Send an interim status message for long-running turns if needed

Depends on:
- T-008
- T-012
- T-014

Source:
- US-07
- Design - Non-command messages
- Design - Response model

Definition of Done:
- Normal user messages execute end to end after agent and project selection
- The final reply is delivered to the correct Telegram chat
- Backend unavailability is reported clearly to the user
- Tests cover the core orchestration path

Verification:
- Run `dotnet test`
- Send a real or controlled Telegram message and confirm a backend-backed reply returns to the same chat

## T-016 - Implement durable approval handling and Telegram approval commands
Status: [ ]
Goal: Pause risky actions for approval, persist the pending record, and resume or deny the correct blocked flow from Telegram.

Scope:
- Persist pending and resolved approval records
- Implement `/approve <id>` and `/deny <id>`
- Resume or terminate the waiting execution flow based on the decision

Depends on:
- T-007
- T-012
- T-014
- T-015

Source:
- PRD - Approval Flow
- US-09
- Design - Approval Model

Definition of Done:
- Risky actions create durable pending approvals before Telegram notification is sent
- Approval and denial commands update the correct record and unblock the correct flow
- Unknown or resolved approval IDs are rejected safely
- Tests cover approval lifecycle and isolation rules

Verification:
- Run `dotnet test`
- Trigger a controlled approval request and verify approve and deny behavior end to end

## T-017 - Add `/projects` and `/status` operator visibility commands
Status: [ ]
Goal: Give the operator enough remote visibility to understand current context and available projects from Telegram.

Scope:
- Implement `/projects`
- Implement `/status`
- Return current agent, project, thread reference summary, and service health summary without leaking unnecessary internals

Depends on:
- T-010
- T-011
- T-013
- T-015

Source:
- PRD - Commands
- US-10

Definition of Done:
- `/projects` lists available projects cleanly
- `/status` shows current agent, project, thread reference summary, and service health summary
- Missing project selection and empty project catalog cases are handled clearly
- Tests cover command output behavior

Verification:
- Run `dotnet test`
- Invoke `/projects` and `/status` through Telegram and confirm the reported state matches persisted state

## T-018 - Harden backend restart and blocked-turn recovery behavior
Status: [ ]
Goal: Make runtime failures survivable by restoring safe behavior after backend crashes, restarts, and service restarts.

Scope:
- Restart the shared backend when it crashes or becomes unhealthy
- Restore or safely fail pending execution and approval state after service restart
- Report interruptions clearly to the operator

Depends on:
- T-013
- T-014
- T-016

Source:
- PRD - Service Hosting Requirements
- Design - Backend recovery
- Design - Durability and restart safety

Definition of Done:
- Backend crash recovery behavior is implemented and observable
- Pending approvals and thread mappings survive service restart safely
- Interrupted active work is surfaced clearly instead of silently disappearing
- Tests cover restart-sensitive flows where practical

Verification:
- Run `dotnet test`
- Simulate backend or service restart during active and approval-blocked flows and confirm safe recovery behavior

## T-019 - Implement maintenance approval classification and audit metadata
Status: [ ]
Goal: Distinguish environment-changing actions from normal risky actions so remote self-management remains auditable and more explicit.

Scope:
- Add a distinct maintenance approval class
- Include stronger warnings and richer summaries for maintenance actions
- Persist approval metadata needed for later audit and reporting

Depends on:
- T-016

Source:
- PRD - Remote self-management
- US-12
- Design - Approval classes

Definition of Done:
- Maintenance approvals are represented distinctly from standard approvals
- Telegram approval messages for maintenance actions are more explicit
- Persisted approval records include the metadata needed to audit these actions
- Tests cover maintenance classification behavior

Verification:
- Run `dotnet test`
- Trigger a controlled maintenance approval flow and inspect the persisted approval record and Telegram summary

## T-020 - Add backend version inspection and Telegram-driven backend update flow
Status: [ ]
Goal: Support the v1 requirement to inspect and update the local `codex-rs` or `codex app-server` runtime from Telegram under approval control.

Scope:
- Expose current backend version information
- Implement an update initiation flow driven from Telegram
- Report target version, outcome, restart result, and required operator follow-up on failure

Depends on:
- T-019
- T-018

Source:
- PRD - Codex Backend
- US-12
- Design - Upgrade handling expectations

Definition of Done:
- The operator can request backend version inspection from Telegram
- A backend update flow exists behind explicit approval
- Update outcome reporting includes version and restart status details
- Tests cover happy-path and failure-path reporting behavior where feasible

Verification:
- Run `dotnet test`
- Execute a controlled backend version inspection and a mocked or controlled update flow

## T-021 - Surface online retrieval capability health and failure reporting
Status: [ ]
Goal: Make the Codex-provided online retrieval dependency visible and understandable in ServantClaw runtime behavior.

Scope:
- Detect or infer whether online retrieval capability appears available
- Surface retrieval-related failures clearly to the operator
- Expose retrieval capability in service health reporting where practical

Depends on:
- T-013
- T-015
- T-017

Source:
- PRD - Online Information Access
- US-13
- Design - Online Information Access

Definition of Done:
- The system can report when online retrieval appears unavailable or failing
- Retrieval-related failures are communicated clearly instead of being opaque backend errors
- Health reporting includes retrieval capability status where practical
- Tests cover retrieval capability reporting behavior

Verification:
- Run `dotnet test`
- Exercise a retrieval-available and retrieval-unavailable path and confirm clear user-facing reporting

## T-022 - Add architecture tests and enforce structural boundaries
Status: [ ]
Goal: Prevent unintended dependency drift as implementation grows by codifying the design's layering rules.

Scope:
- Add architecture tests using the chosen structural verification approach
- Enforce key boundaries between domain, application, infrastructure, and transport code
- Fail CI or local verification when forbidden references are introduced

Depends on:
- T-002
- T-003
- T-013
- T-015

Source:
- Design - Linters and structural verification

Definition of Done:
- Architecture tests enforce at least the most important dependency rules from the design
- Structural rule failures are easy to understand and fix
- Verification commands include the architecture tests

Verification:
- Run `dotnet test`
- Intentionally validate that a forbidden dependency would fail the architecture test suite

## T-023 - Add mutation testing for safety-critical logic
Status: [ ]
Goal: Validate that the most important safety and routing tests actually detect behavioral changes.

Scope:
- Configure `Stryker.NET`
- Target safety-critical areas first, including routing, thread isolation, approval gating, queue serialization, and startup validation
- Document how mutation testing is run for this repo

Depends on:
- T-002
- T-012
- T-016
- T-022

Source:
- Design - Mutation testing

Definition of Done:
- Mutation testing is configured and runnable for at least one safety-critical project or slice
- Initial mutation targets cover the most important safety-sensitive logic
- Repository guidance exists for running the mutation suite

Verification:
- Run the configured mutation test command for the targeted scope
- Confirm the mutation run completes and reports usable results
