# Repository Guidelines

## Project Structure & Module Organization
`ServantClaw.sln` is the root solution. Production code lives under `src/` in layered projects: `ServantClaw.Host` (worker entrypoint and configuration), `Application`, `Domain`, `Infrastructure`, plus integration-facing modules `Telegram` and `Codex`. Tests live under `tests/` with separate `ServantClaw.UnitTests` and `ServantClaw.IntegrationTests` projects. Planning artifacts such as `prd.md`, `design.md`, `tasks.md`, and `user-stories.md` describe scope and should stay aligned with code changes.

## Build, Test, and Development Commands
Run commands from the repository root.

```powershell
dotnet restore ServantClaw.sln
dotnet build ServantClaw.sln -c Debug
dotnet run --project src/ServantClaw.Host/ServantClaw.Host.csproj
dotnet test ServantClaw.sln
```

`restore` resolves NuGet packages. `build` validates the full solution. `run` starts the hosted worker defined in `src/ServantClaw.Host/Program.cs`; the host copies `appsettings*.json` to its output directory and validates those JSON settings at startup. `test` runs the unit, integration, and architecture suites.

## Coding Style & Naming Conventions
This is a C#/.NET 9 codebase with `Nullable` and `ImplicitUsings` enabled in every project. Follow standard C# conventions: 4-space indentation, file-scoped namespaces, PascalCase for types and public members, camelCase for locals and parameters, and one public type per file where practical. Keep project boundaries clean: domain logic in `Domain`, orchestration in `Application`, adapters in `Infrastructure` or channel-specific projects.

## Testing Guidelines
Place unit tests in `tests/ServantClaw.UnitTests` and broader host or wiring checks in `tests/ServantClaw.IntegrationTests`. Name test files after the subject under test, for example `WorkerTests.cs` or `TelegramAdapterTests.cs`. If you add real tests, add the matching test framework and `Microsoft.NET.Test.Sdk` to the relevant test project and keep `dotnet test ServantClaw.sln` passing before opening a PR.

## Mutation Testing
Mutation testing validates that tests actually detect behavioral change. Stryker.NET is the configured tool, pinned via `.config/dotnet-tools.json` and restored with `dotnet tool restore`.

Run on a safety-critical project by entering its directory and invoking the pinned tool:

```powershell
cd src/ServantClaw.Domain; dotnet dotnet-stryker
cd src/ServantClaw.Application; dotnet dotnet-stryker
```

Per-task runs use `--since:main` to mutate only files changed against the target branch. The protocol for when to run and how to handle results lives in `.claude/skills/implement-next-task/references/mutation-check-protocol.md`.

**The mutation check is a hard gate.** `thresholds.break = 100` in each config, so a run fails if any mutant survives or lacks coverage. For every mutant, pick one of:

- **Kill it with a test** — grounded in expected behavior from `design.md` / `user-stories.md` / `prd.md`, not whatever the current code happens to do
- **Disable inline** with `// Stryker disable once <Mutator> : equivalent - <reason>` or `// Stryker disable once <Mutator> : low-value - <reason>` — Stryker requires the `:` reason text
- **Exclude a whole type** with `[ExcludeFromCodeCoverage]` when its safety-critical behavior is owned by a different task (comment should point at that task — e.g. T-012/T-015/T-016/T-023)

Configs also apply `ignore-methods` for logging calls and exception constructors to suppress known noise. Per-project configs live at `src/<Project>/stryker-config.json`. T-023 will expand scope to all safety-critical slices named in `design.md` and prepare CI enforcement.

## Codex Backend Transport
The transport adapter in `src/ServantClaw.Codex/Transport` talks to a local `codex app-server` over `stdio`. A few long-lived constraints future work must respect:

- The supervisor publishes a `BackendSession` through `IBackendSessionPublisher` (`src/ServantClaw.Application/Runtime`) whenever a process starts cleanly, and retracts + cancels the session's `SessionLifetime` token on exit. Transport code subscribes via `IBackendSessionSource`. Do not reach for the `BackendProcessSupervisor` concrete type or the `IBackendProcessHandle` streams from anywhere else.
- Codex uses JSON-RPC 2.0 **lite** — no `"jsonrpc": "2.0"` field on the wire, JSONL framing, and **both sides originate requests**. Approvals (`item/commandExecution/requestApproval`, `item/fileChange/requestApproval`) arrive as server→client requests; answer them by `SendResponseAsync(requestId, "accept" | "decline", ...)` on `StdioJsonRpcConnection`. Unknown server methods get a JSON-RPC `-32601` error response — do not ignore, the server will block on them.
- `StdioCodexBackendClient` serializes turns **globally** (a single `activeTurn` per client instance). Codex's streamed `item/*` events do not carry a `threadId`, so multiplexing per-context turn aggregation would be unsafe. The per-context queue still guarantees ordering within a `ThreadContext`; across contexts, turns run serially on the shared backend.
- `SendTurnAsync` requires a prior `CreateThreadAsync` or `ResumeThreadAsync` to establish the current thread on the client. T-015's wiring must honour that call order.
- `initialize` + `initialized` run exactly once per session (re-run if the session is replaced). Session loss fails pending requests with `BackendUnavailableException` and does not auto-replay; durable recovery is T-018's scope.

## Commit & Pull Request Guidelines
Recent history uses short, imperative commit subjects such as `Scaffold ServantClaw solution structure`. Keep that style: concise, capitalized, and focused on one change. PRs should explain what changed, why it changed, and how it was verified. Link the relevant task or issue, and include config notes or sample output when behavior changes.
