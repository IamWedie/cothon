# ADR-0001: Stack — Avalonia + .NET 10, single C# core

## Status
Accepted

## Context
Need a Windows-first personal daily driver, with room to evolve.

## Decision
- UI: Avalonia
- Core: single C# process (.NET 10)
- DI: Microsoft.Extensions.Hosting
- MVVM: CommunityToolkit.Mvvm
- Tools: MCP first with a thin broker
- Voice: provider adapter (start Gemini Live)
- Platform glue: Windows-specific in src/Platforms/Windows

## Consequences
- Unify C#/UI, eliminate Python/IPC drift
- Easier testing
