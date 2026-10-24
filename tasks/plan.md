# Implementation Plan: Cothon

## Overview
Build a personal daily-driver: tray-first, quiet UI, single C# core, MCP tools, provider-abstracted voice.

## Architecture Decisions
- Avalonia UI, MVVM
- Host builder as composition root
- Tool broker: JSON Schema + risk + capabilities + reversible + preview + undoToken
- Memory: events -> facts with provenance/TTL
- Policy: capability scopes + approval previews
- Port hardened ADB logic from ULTRON
