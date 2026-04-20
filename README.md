# ServantClaw

ServantClaw is a local-first `.NET 9` worker service that turns a Windows machine into a single-user Telegram front end for a Codex-backed assistant. The long-term goal is to let one approved Telegram user interact with `general` and `coding` agents across multiple named projects while keeping routing, approvals, and thread isolation explicit and auditable.

The current repository contains the solution structure, domain contracts, host wiring, startup configuration validation, and baseline test coverage needed to build out that v1 design safely.

## What It Is

- A Windows-hosted worker service built on `Microsoft.Extensions.Hosting`
- A Telegram-to-Codex bridge intended to run headlessly as a Windows Service
- A layered codebase with separate `Domain`, `Application`, `Infrastructure`, `Telegram`, `Codex`, and `Host` projects
- A local-first system designed around explicit project routing, restart-safe approvals, and durable state

## Current Status

Implemented today:

- Solution and project structure for the v1 architecture
- Core domain contracts for agents, approvals, routing, state, and backend interaction
- Host startup wiring and Windows Service integration
- JSON-backed configuration loading with validation on startup
- Unit, integration, and architecture tests for the current baseline

Not implemented yet:

- Telegram command handling and polling runtime
- Codex backend process supervision and JSON-RPC transport
- Persistent JSON state storage
- Approval workflows, routing logic, and project binding behavior
- Durable service logging and Windows Event Log reporting beyond the current host baseline

## Solution Layout

```text
src/
  ServantClaw.Application     Application orchestration and use cases
  ServantClaw.Codex           Codex-facing integration layer
  ServantClaw.Domain          Core contracts and domain models
  ServantClaw.Host            Worker entrypoint, DI, config, and runtime composition
  ServantClaw.Infrastructure  Shared infrastructure adapters
  ServantClaw.Telegram        Telegram-facing integration layer

tests/
  ServantClaw.ArchitectureTests
  ServantClaw.IntegrationTests
  ServantClaw.UnitTests
```

Planning and design artifacts live in the repository root:

- `prd.md`
- `design.md`
- `tasks.md`
- `user-stories.md`
- `AGENTS.md`

## Architecture Notes

ServantClaw follows a layered design:

- `Domain` holds pure contracts and state models such as agent kinds, approvals, routing state, and backend abstractions.
- `Application` is reserved for orchestration and use-case logic.
- `Infrastructure`, `Telegram`, and `Codex` are adapter layers.
- `Host` is the composition root that wires configuration, dependency injection, and Windows Service hosting.

The v1 design is intentionally local-first:

- one approved Telegram user
- one Windows Service process
- one local `codex app-server` backend over `stdio` JSON-RPC
- file-based state
- explicit project selection when routing work

## Requirements

- Windows machine for the intended service-hosting scenario
- `.NET SDK 9`
- A Telegram bot token created through BotFather
- A locally installed Codex runtime that can run `codex app-server`

## Getting Started

From the repository root:

```powershell
dotnet restore ServantClaw.sln
dotnet build ServantClaw.sln -c Debug
dotnet test ServantClaw.sln
dotnet run --project src/ServantClaw.Host/ServantClaw.Host.csproj
```

`dotnet run` starts the worker host locally. On startup, the host validates configuration before runtime services begin.

## Configuration

The host project includes:

- `src/ServantClaw.Host/appsettings.json`
- `src/ServantClaw.Host/appsettings.Development.json`

The default `appsettings.json` contains placeholders that must be replaced before startup succeeds. The current configuration model expects values for:

- `Service:BotRootPath`
- `Service:ProjectsRootPath`
- `Service:Backend:ExecutablePath`
- `Service:Backend:WorkingDirectory`
- `Service:Backend:Arguments`
- `Telegram:BotToken`
- `Telegram:Polling:Timeout`
- `Telegram:Polling:RetryDelay`
- `Owner:UserId`
- `Owner:Username`

Example shape:

```json
{
  "Service": {
    "BotRootPath": "C:\\ServantClaw\\bot-root",
    "ProjectsRootPath": "C:\\ServantClaw\\projects",
    "Backend": {
      "ExecutablePath": "C:\\tools\\codex.exe",
      "WorkingDirectory": "C:\\ServantClaw",
      "Arguments": [
        "app-server"
      ]
    }
  },
  "Telegram": {
    "BotToken": "123456:replace-me",
    "Polling": {
      "Timeout": "00:00:30",
      "RetryDelay": "00:00:05"
    }
  },
  "Owner": {
    "UserId": 42,
    "Username": "approved-owner"
  }
}
```

If placeholder or required values are missing, the host fails fast during startup.

## Testing

Run the full suite with:

```powershell
dotnet test ServantClaw.sln
```

The repository currently includes:

- unit tests for core contracts and solution references
- integration tests for startup validation and host lifecycle behavior
- architecture tests that protect project dependency boundaries

## Development Notes

- Nullable reference types and implicit usings are enabled across the solution.
- The codebase uses `xUnit`, `FluentAssertions`, and `NSubstitute`.
- Repository guidance for contributors and agents lives in `AGENTS.md`.
- The backlog is tracked in `tasks.md`, with `T-006` currently being the next planned task.

## Roadmap Direction

The intended v1 product is a trustworthy remote assistant that can:

- receive commands through Telegram
- route work to `general` or `coding` agents
- keep thread history isolated by chat, project, and agent
- request Telegram approval for risky actions
- manage the local Codex environment without requiring desktop interaction

The present codebase establishes the host and architectural baseline for those features.
