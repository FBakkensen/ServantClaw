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

## Commit & Pull Request Guidelines
Recent history uses short, imperative commit subjects such as `Scaffold ServantClaw solution structure`. Keep that style: concise, capitalized, and focused on one change. PRs should explain what changed, why it changed, and how it was verified. Link the relevant task or issue, and include config notes or sample output when behavior changes.
