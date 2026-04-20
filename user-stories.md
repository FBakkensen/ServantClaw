# User Stories

## US-01: Register a Telegram bot and connect its token to ServantClaw

**Story:** As the bot owner, I want to create a Telegram bot with BotFather and provide its generated token to ServantClaw, so that the service can authenticate to Telegram and start operating through that bot identity.

**Rationale:** This is the missing onboarding step before any chat-based workflow can exist. The official Telegram Bot API docs state that each bot is assigned a unique authentication token upon creation, and that token is the credential an application must use to interact with the Bot API.

**Acceptance Criteria**
- The onboarding flow instructs the user to create one Telegram bot through `@BotFather` using the `/newbot` flow before starting normal ServantClaw operation.
- The onboarding flow requires the user to choose the bot's display name and username during BotFather setup.
- ServantClaw provides a clear configuration step for supplying the generated bot token to the local installation.
- The supplied token is persisted locally in bot configuration under the bot root.
- On startup, ServantClaw uses the configured token to authenticate to the Telegram Bot API.
- If the token is invalid or missing, startup or bot initialization fails clearly and points to bot setup/configuration.

**Edge Cases / Safety Constraints**
- V1 must support exactly one active Telegram bot token per running instance.
- Token values must be treated as secrets in storage and logs and must never be echoed back in normal operator-visible responses.
- If the token is rotated in Telegram, ServantClaw must require the updated token before normal bot operation can resume.
- The onboarding flow should make it easy to verify that the configured token corresponds to the intended bot identity before normal operation begins.

**Dependencies**
- None

## US-02: Connect the bot to Telegram as the approved owner

**Story:** As the approved Telegram user, I want the bot to accept and respond to my messages while rejecting other users, so that I can control the system remotely without exposing it to unauthorized access.

**Rationale:** This is the minimum secure entry point for the product. Without verified owner-only access, every later capability is unsafe.

**Acceptance Criteria**
- The service can start one configured Telegram bot and receive messages from Telegram.
- Messages from the approved Telegram user are accepted for normal processing.
- Messages from any other Telegram user are rejected or ignored safely.
- The approved Telegram user identity is persisted locally and survives service restarts.
- The system writes logs showing bot startup and inbound message handling outcomes.

**Edge Cases / Safety Constraints**
- If no approved user is configured yet, the system must fail safely or remain unavailable until configured.
- Unauthorized users must not receive sensitive internal details in rejection responses.
- The system must support exactly one active Telegram bot per running instance in v1.

**Dependencies**
- US-01

## US-03: Select the active agent for a chat

**Story:** As the approved Telegram user, I want to switch the active agent for a Telegram chat, so that I can choose whether that chat uses the `general` or `coding` assistant.

**Rationale:** Agent choice is a core user-visible control surface and must be explicit before requests are routed into isolated contexts.

**Acceptance Criteria**
- The bot supports an `/agent <agent-id>` command for the current Telegram chat.
- The command accepts exactly `general` and `coding` in v1.
- The selected active agent is persisted per Telegram chat.
- After switching agents, subsequent messages in that chat route to the newly selected agent.
- The bot confirms the active agent after a successful switch.

**Edge Cases / Safety Constraints**
- Invalid agent IDs must be rejected with a clear user-facing error.
- Switching the active agent must not modify project bindings or threads belonging to the other agent.
- Agent state must remain isolated across agents even within the same Telegram chat.

**Dependencies**
- US-02

## US-04: Bind a chat to a project for a specific agent

**Story:** As the approved Telegram user, I want to bind the current chat to a named project for a chosen agent, so that work runs in the correct project context.

**Rationale:** Explicit project binding is central to the product's isolation model and prevents work from being routed into the wrong local folder.

**Acceptance Criteria**
- The bot supports `/project <agent-id> <project-id>` to bind or switch the project for that agent in the current chat.
- Project bindings are persisted per `(agent, telegram chat)` pair.
- After a successful bind, subsequent messages for that agent in that chat use the selected project context.
- The bot confirms the active project after a successful bind.
- Projects are represented as local folders under the bot root.

**Edge Cases / Safety Constraints**
- The system must reject unknown project IDs with a clear error.
- Changing the bound project for one agent must not affect the other agent's binding in the same chat.
- Project selection must remain explicit by default rather than silently inferred.

**Dependencies**
- US-02
- US-03

## US-05: Confirm project selection when routing is ambiguous

**Story:** As the approved Telegram user, I want the bot to ask for confirmation when it cannot confidently determine the intended project, so that work is not silently routed to the wrong place.

**Rationale:** The PRD makes ambiguity handling a safety rule, not a convenience feature. This story turns that rule into a concrete user-facing flow.

**Acceptance Criteria**
- If no project is selected for the relevant agent and chat, the bot does not start work immediately.
- If the user request implies multiple plausible projects, the bot presents candidate projects and asks for confirmation.
- Work is routed only after the user explicitly confirms the intended project.
- Once confirmed, the chosen project becomes the active binding for that `(agent, telegram chat)` context.
- The bot does not silently guess a project in ambiguous cases.

**Edge Cases / Safety Constraints**
- If there are no plausible projects, the bot must ask the user to choose rather than inventing one.
- A confirmation flow for one agent must not alter bindings for the other agent.
- Candidate suggestions must be human-readable and clearly distinguished from confirmed routing.

**Dependencies**
- US-03
- US-04

## US-06: Route each chat-agent-project context to its own persistent Codex thread

**Story:** As the approved Telegram user, I want each `(agent, project, telegram chat)` context to keep its own Codex thread, so that conversations stay isolated and retain the right history.

**Rationale:** Thread isolation is the backbone of the product. It is what makes multi-project and multi-agent behavior trustworthy instead of cross-contaminated.

**Acceptance Criteria**
- The system creates or resumes one Codex thread per `(agent, project, telegram chat)` triple.
- Thread mappings are persisted locally and survive service restarts.
- Messages sent within the same triple resume the same thread by default.
- Different projects in the same chat use different threads.
- Different agents in the same chat use different threads.

**Edge Cases / Safety Constraints**
- The system must not reuse a thread across different projects, agents, or Telegram chats.
- Missing or corrupted thread mapping state must fail safely and avoid misrouting into another thread.
- Thread persistence must not depend on an interactive terminal session.

**Dependencies**
- US-03
- US-04

## US-07: Send messages from Telegram to the Codex backend and return responses

**Story:** As the approved Telegram user, I want my Telegram messages to be processed by the selected Codex-backed assistant and returned to the chat, so that the bot is useful as a remote working interface.

**Rationale:** This is the primary end-to-end product behavior. The earlier stories establish safe routing; this story makes the assistant actually usable.

**Acceptance Criteria**
- After agent and project context are established, a user message is sent to the Codex backend for the correct thread.
- The bot returns the assistant response back into the originating Telegram chat.
- The system communicates with the local Codex backend through an internal backend abstraction.
- V1 uses `codex app-server` over `stdio` JSON-RPC behind that abstraction.
- The system logs backend startup and connectivity events relevant to message handling.

**Edge Cases / Safety Constraints**
- If the backend is unavailable, the bot must report the failure clearly instead of pretending work completed.
- Backend communication must not rely on the interactive Codex CLI user experience during normal runtime.
- A response for one chat context must never be delivered into another chat.

**Dependencies**
- US-06

## US-08: Start a fresh thread with `/clear` without deleting prior history

**Story:** As the approved Telegram user, I want to clear the current working context and start a fresh thread, so that I can reset conversation state without losing the prior record.

**Rationale:** Context reset is a required control for long-running assistant use, but the PRD explicitly forbids destructive history deletion.

**Acceptance Criteria**
- The bot supports `/clear` for the current `(agent, project, telegram chat)` context.
- Executing `/clear` creates a new Codex thread mapping for that context.
- Future messages in that context use the new thread by default.
- Prior thread references remain preserved in local state.
- The bot confirms that a fresh thread has started.

**Edge Cases / Safety Constraints**
- `/clear` must not delete or overwrite prior thread history.
- `/clear` in one context must not affect threads for other agents, projects, or chats.
- If no project is currently bound, the system must fail safely and explain what context is missing.

**Dependencies**
- US-06
- US-07

## US-09: Approve or deny risky actions from Telegram

**Story:** As the approved Telegram user, I want risky assistant actions to pause for approval and then be approved or denied from Telegram, so that high-impact operations stay under my control.

**Rationale:** Approval gating is one of the main safety promises of the product and is especially important because the system runs unattended as a service.

**Acceptance Criteria**
- When the assistant requests a risky action, the bot pauses execution and creates a pending approval record.
- The bot sends a Telegram message containing an approval ID and a short human-readable summary.
- The bot supports `/approve <id>` and `/deny <id>` commands.
- Approving or denying an action updates the pending record and unblocks the correct waiting flow.
- Approval decisions are persisted locally and auditable after the fact.

**Edge Cases / Safety Constraints**
- Unknown, expired, or already-resolved approval IDs must be rejected safely.
- Only the approved Telegram user may approve or deny actions.
- Approval actions must apply only to the matching pending request and must not leak across chats, agents, or projects.

**Dependencies**
- US-02
- US-07

## US-10: View current working context and available projects

**Story:** As the approved Telegram user, I want to inspect the current bot context and available projects from Telegram, so that I can understand where work will run before sending more instructions.

**Rationale:** Lightweight visibility reduces routing mistakes and makes the system operable from a phone without requiring local machine access.

**Acceptance Criteria**
- The bot supports `/projects` to list available projects for the active or specified agent.
- The bot supports `/status` to show the current agent, current project, current thread reference, and service health summary.
- Returned project and status information reflects persisted local state.
- The commands work from Telegram without requiring direct filesystem or desktop access.

**Edge Cases / Safety Constraints**
- If no project is selected, `/status` must show that clearly rather than implying a default.
- If there are no projects for the requested agent, `/projects` must report that cleanly.
- Status output must avoid leaking unnecessary internal details that do not help the approved operator.

**Dependencies**
- US-03
- US-04
- US-06

## US-11: Keep the bot running reliably as a Windows Service

**Story:** As the approved Telegram user, I want the bot to run unattended as a Windows Service, so that it remains available remotely without needing an active desktop session.

**Rationale:** Service hosting is a core product promise, not an implementation detail. The assistant is only useful remotely if it survives reboots and runs headlessly.

**Acceptance Criteria**
- The application can be installed and run as a Windows Service.
- The service starts cleanly without requiring an interactive desktop session.
- The service can launch the Telegram bot and Codex backend as part of normal startup.
- The service supports clean shutdown and restart behavior.
- Durable logs are written for startup, shutdown, and runtime troubleshooting.

**Edge Cases / Safety Constraints**
- Startup failures must be logged clearly enough to troubleshoot remotely or locally later.
- Normal operation must not assume an interactive terminal is available.
- The service must handle restarts without corrupting persisted file-based state.

**Dependencies**
- US-02
- US-07

## US-12: Maintain the Codex environment remotely from Telegram

**Story:** As the approved Telegram user, I want to update the local Codex environment from Telegram, so that I can add or change MCP servers, skills, related configuration, and the local `codex-rs` server runtime without local desktop access.

**Rationale:** Remote self-management is a first-class product capability in the PRD because the system is intended to run headlessly while the operator is remote.

**Acceptance Criteria**
- The assistant can initiate flows to add or update MCP server configuration from Telegram.
- The assistant can initiate flows to add, update, or remove Codex skills or related local agent assets from Telegram.
- The assistant can initiate a Telegram-driven update of the local `codex-rs` server / `codex app-server` runtime to a newer released version.
- The assistant can show the currently installed backend version and the requested target version as part of the update flow.
- The assistant can reload, restart, or otherwise refresh Codex-side processes when required by configuration changes.
- These operations work through the normal Telegram-driven workflow without requiring local desktop interaction.
- Changes and related approvals are logged durably.

**Edge Cases / Safety Constraints**
- Environment-changing actions must remain subject to approval controls where appropriate.
- Runtime upgrade actions for `codex-rs` / `codex app-server` must require explicit approval before execution.
- Failed configuration changes must report clear outcomes rather than leaving the user uncertain whether the environment changed.
- Failed backend upgrades must report whether the running version changed, whether restart succeeded, and what operator action is required next.
- The system must define a safe boundary so remote maintenance does not become silent unrestricted self-modification.

**Dependencies**
- US-09
- US-11

## US-13: Retrieve current information from the internet through the assistant

**Story:** As the approved Telegram user, I want the assistant to access current online information when needed, so that it is not limited to local files and stale context.

**Rationale:** Online retrieval is a core capability in the PRD and materially changes the assistant from a local shell to a more useful remote operator tool.

**Acceptance Criteria**
- The assistant can perform web search or equivalent online retrieval through the Telegram-driven workflow.
- Retrieved online information can be used in assistant responses back to Telegram.
- The architecture supports configuring or extending online retrieval without redesigning the rest of the system.
- Online retrieval works within the same safety model used by the broader assistant where appropriate.

**Edge Cases / Safety Constraints**
- If online retrieval is unavailable, the bot must report that limitation clearly.
- Web access and online-information actions must not bypass approval controls when those controls apply.
- Online retrieval capability must not be coupled so tightly to one implementation that future extension becomes impractical.

**Dependencies**
- US-07
- US-11
