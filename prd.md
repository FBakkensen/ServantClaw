# Product Requirements Document

## Product Name

ServantClaw v1

## Summary

ServantClaw v1 is a single-user Telegram bot that acts as a remote front end to a local Codex-backed assistant running on Windows as a service. It supports two agent types, `general` and `coding`, and both agents can operate across multiple named projects. The system is designed to start small, stay local-first, and preserve strong isolation between agents, projects, chats, and approval flows.

The first version must be operationally simple and trustworthy:

- One running Telegram bot per instance
- One approved Telegram user
- One Windows service process
- Local `codex app-server` backend over `stdio` JSON-RPC
- File-based state only
- Explicit project routing with confirmation when ambiguous
- Telegram approval flow for risky actions

## Goals

- Provide a practical remote assistant reachable from Telegram
- Use a ChatGPT-backed Codex setup rather than requiring a separate hand-built API integration for v1
- Support both a `general` agent and a `coding` agent behind one bot
- Support multiple named projects for both agents
- Preserve isolation so the wrong task does not run in the wrong context
- Run unattended as a Windows Service
- Keep the architecture open to future transport changes and multi-bot expansion

## Non-Goals

- Multi-user support
- Multiple concurrently active Telegram bots in one v1 process
- Database-backed persistence
- Full OpenClaw-style multi-channel platform support
- Automatic project routing without user confirmation
- Destructive history deletion on `/clear`
- Production use of `codex app-server` websocket transport in v1

## Users

### Primary user

- The single bot owner/operator using Telegram as the control surface

### Future users, not in scope for v1

- Additional Telegram users
- Additional Telegram bots
- Other channels beyond Telegram

## Product Principles

- Local-first: state and control live on the machine running the service
- Explicit over implicit: project binding and risky actions must be visible
- Isolation by default: thread, agent, and project state must not bleed together
- Small but extensible: v1 should be simple while preserving clean upgrade paths
- Headless by design: no dependence on an interactive desktop session during normal operation
- Self-extensible from Telegram: because the operator is remote and the system runs headless, the assistant must be able to maintain and extend its own Codex environment, including adding MCP servers, adding or updating skills, updating the local `codex-rs` server / `codex app-server` runtime when OpenAI releases a new version, and reloading or restarting Codex-side processes when required, subject to approval controls
- Online-aware: the assistant must be able to retrieve current information from the web so it is not limited to local state and static context

## Core User Experience

The user interacts with one Telegram bot. Within a chat, they can work with either the `general` agent or the `coding` agent. Each agent can access multiple named projects. Before work begins, the relevant project for the current chat and agent must be explicit. If the user has not selected one, the bot may suggest likely matches but must ask for confirmation before proceeding.

If the user wants to reset context, they can use `/clear`, which starts a new Codex thread for the current chat context without deleting old history. If the underlying agent requests a risky action, the bot pauses and asks for approval in Telegram using a simple approval command.

## Functional Requirements

### 1. Telegram Bot

- The system must support exactly one active Telegram bot per running instance in v1.
- The Telegram bot must be created via BotFather and configured using the issued bot token.
- The architecture must keep bot registration/config generic enough to support multiple bots in the future.
- The bot must run as part of a long-lived Windows Service process.

### 2. Single-User Access Control

- The system must allow only one approved Telegram user in v1.
- Messages from all other Telegram users must be rejected or ignored safely.
- The approved user identity must be persisted locally.

### 3. Agents

- The system must expose two logical agents:
  - `general`
  - `coding`
- The active agent must be selectable per Telegram chat.
- Agent state must be isolated from one another.
- Each agent must have its own projects, thread mappings, logs, and approval state.

### 4. Projects

- Both `general` and `coding` agents must support multiple named projects.
- Projects must be represented as local folders under the bot root.
- A Telegram chat must bind to one selected project at a time for each agent.
- Project selection must be explicit by default.
- If no project is selected or the user message implies a project ambiguously, the bot may suggest candidates but must ask for confirmation before routing work.
- The system must not silently guess the project in ambiguous cases.

### 5. Thread Model

- Each `(agent, project, telegram chat)` triple must map to its own Codex thread history by default.
- The system must persist the mapping between chat context and Codex thread.
- `/clear` must create a new thread for the current `(agent, project, telegram chat)` context.
- `/clear` must preserve old thread history rather than deleting it.

### 6. Codex Backend

- The bot must communicate with a local Codex backend via `codex app-server`.
- V1 must use `stdio` JSON-RPC transport only.
- The internal architecture must abstract backend transport behind a stable interface so a future transport can be added without rewriting bot logic.
- The system must not depend on the Codex CLI interactive user experience for normal runtime operation.
- The system must support remote Codex environment management from Telegram, including:
  - adding or updating MCP server configuration
  - adding, updating, or removing Codex skills or related local agent assets
  - updating the local `codex-rs` server / `codex app-server` runtime to a newer released version
  - reloading, restarting, or otherwise refreshing the Codex backend when configuration changes require it
- These management actions must work without requiring local desktop interaction during normal operation.

### 7. Online Information Access

- The system must provide a way for the assistant to search the web and retrieve online information.
- This capability must be available remotely through the Telegram-driven workflow.
- The design may satisfy this through built-in Codex capabilities, MCP servers, skills, or another compatible extension path.
- The architecture must allow online information access to be configured, updated, or extended without redesigning the whole system.
- Web and online-information actions should remain subject to the same safety and approval model where appropriate.

### 7. Approval Flow

- If Codex requests a risky action, the bot must pause and ask for approval in Telegram.
- Approval UX in v1 must be coarse-grained and simple.
- The bot must support commands equivalent to:
  - `/approve <id>`
  - `/deny <id>`
- Approval requests must include a short human-readable summary.
- Approval records must be persisted locally.

### 8. Commands

V1 must support a minimal slash-command set.

Required commands:

- `/clear`
  - Start a fresh thread for the current `(agent, project, chat)` context
- `/agent <agent-id>`
  - Switch active agent for the current chat
- `/project <agent-id> <project-id>`
  - Bind or switch the current project for the current chat and target agent
- `/approve <id>`
  - Approve a pending action
- `/deny <id>`
  - Deny a pending action

Nice-to-have commands for v1 if implementation cost is low:

- `/projects`
  - List available projects for the active or specified agent
- `/status`
  - Show current agent, project, thread, and service health summary

## Storage Requirements

All state must be stored in local files under one bot root directory.

The design must support a folder layout conceptually similar to:

```text
bot-root/
  config/
  telegram/
  approvals/
  logs/
  general/
    projects/
      <project-id>/
    chats/
    threads/
  coding/
    projects/
      <project-id>/
    chats/
    threads/
```

Requirements:

- No database in v1
- Human-inspectable state where practical
- Durable persistence across service restarts
- Corrupted machine-managed state must preserve recovery artifacts and diagnostics instead of requiring manual JSON repair as the only path forward
- Separate storage for:
  - bot configuration
  - Telegram identity/bindings
  - agent/project mappings
  - thread references
  - approvals
  - logs

## Service Hosting Requirements

- The application must run as a Windows Service in v1.
- It must start cleanly without requiring an active desktop session.
- It must support clean shutdown and restart behavior.
- It must write durable logs suitable for service operation and troubleshooting.
- It must not assume an interactive terminal is available during normal runtime.

## Architecture Requirements

### Required architecture shape

- Telegram adapter layer
- Bot orchestration layer
- Agent/project/thread routing layer
- Approval coordinator
- Codex backend abstraction
- `stdio` transport adapter for `codex app-server`
- Codex environment management layer
- Online information retrieval layer
- File-based state store
- Windows Service host

### Transport abstraction

The system must isolate Codex communication behind an internal interface such as:

- start/resume thread
- send turn/input
- receive streaming updates
- handle approval requests
- inspect backend health

This abstraction must make it possible to add a later transport without rewriting:

- Telegram command handling
- state management
- routing logic
- approval flow

### Remote self-management

Because the product is intended to run headless while the user primarily interacts from Telegram on a phone, the architecture must support safe remote maintenance of the local Codex environment.

This includes:

- adding MCP servers
- updating MCP configuration
- adding or updating skills
- updating the local `codex-rs` server / `codex app-server` runtime when a newer release is available
- reloading or restarting Codex-side services when required by those changes

These flows should be treated as first-class product capabilities rather than ad hoc operator tasks.

### Online retrieval

The architecture must support current-information retrieval from the internet.

This includes:

- searching the web
- retrieving relevant pages or sources
- making the capability available to the assistant from Telegram

This should be treated as a core assistant capability, not a later add-on.

### Isolation model

Isolation is defined by:

- agent
- project
- Telegram chat

The default durable context unit is:

- one Codex thread per `(agent, project, telegram chat)` triple

## Safety Requirements

- The system must never silently route ambiguous tasks to a guessed project.
- The system must require Telegram approval for risky actions.
- Non-owner Telegram users must not be able to control the bot.
- `/clear` must not destructively erase prior history.
- Approval actions must be auditable after the fact.
- The operator must retain a path to communicate with Codex for guided self-repair even when normal persisted routing state is damaged.

## Observability Requirements

V1 should provide enough visibility to operate reliably as a service.

Minimum observability requirements:

- service startup/shutdown logs
- Telegram webhook or polling status logs
- Codex backend launch and connectivity logs
- approval request and decision logs
- command usage logs
- error logs with enough detail for debugging

## Open Questions and Risks

### 1. ChatGPT/Codex authentication bootstrap

The biggest known implementation risk is how the ChatGPT-backed Codex sign-in flow is bootstrapped for a Windows Service. The service itself should run unattended, but the initial credential setup may require an interactive login step.

### 2. Tool policy for the `general` agent

The exact tool envelope for the `general` agent is still open. It should be useful, but it should not quietly become unrestricted automation without intentional design.

### 3. Codex app-server operational maturity

V1 assumes local `stdio` JSON-RPC is the most stable app-server integration point. Future transport changes must be possible, but v1 should not rely on experimental websocket behavior.

### 4. Safe self-modification boundary

The product now explicitly requires the ability to modify its own Codex environment remotely. That creates a second-order safety problem: we need clear approval and audit rules for environment-changing operations such as adding MCP servers, changing skills, or restarting backend processes.

An additional operational risk is safe remote upgrade handling for the local `codex-rs` server / `codex app-server` runtime. We need a clear upgrade flow, version visibility, failure reporting, and rollback or recovery expectations when an update does not complete cleanly.

### 5. Self-repair path under damaged state

Because the product is operated remotely over Telegram, local file corruption cannot assume manual operator repair on disk. V1 therefore needs an explicit self-repair path that can still reach Codex, carry forward quarantine diagnostics, and help the operator recover damaged state without trusting the corrupted routing records themselves.

## Acceptance Criteria

ServantClaw v1 is complete when all of the following are true:

- A Windows Service can start and host the bot successfully
- One Telegram bot can receive and respond to messages from the approved user
- The bot can switch between `general` and `coding` agents
- Both agents support multiple named projects
- Project selection is explicit, with suggestion plus confirmation when ambiguous
- Each `(agent, project, telegram chat)` gets its own persistent Codex thread mapping
- `/clear` rotates to a fresh thread without deleting prior history
- Risky actions pause and request Telegram approval
- Approval and denial commands work end-to-end
- The assistant can access online information through a web search / retrieval capability
- The operator can trigger an update of the local `codex-rs` server / `codex app-server` runtime from Telegram when a newer release is needed
- All state persists in files under the bot root
- Corrupted persisted state does not strand the operator and instead preserves a Codex-assisted recovery path with usable diagnostics
- The Codex backend is integrated through a transport abstraction, with `stdio` implemented in v1

## References

- OpenAI Help: Using Codex with your ChatGPT plan
  - https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan
- OpenAI Help: Codex CLI and Sign in with ChatGPT
  - https://help.openai.com/en/articles/11381614-codex-codex-andsign-in-with-chatgpt
- OpenAI Codex documentation
  - https://platform.openai.com/docs/codex
- OpenAI Codex app-server README
  - https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md
