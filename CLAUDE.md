# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BODA VMS (Vision Management System) — the **Web Solution** portion of a hybrid industrial vision inspection system. This repo contains only the web management layer; a separate .NET Framework 4.8.1 Vision Engine handles real-time inspection via Cognex VisionPro.

Both systems share a **SQLite database (BodaVision.db)** using WAL mode for concurrent access.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build BODA.VMS.Web.slnx

# Run the server (launches both server and WASM client)
dotnet run --project BODA.VMS.Web/BODA.VMS.Web/BODA.VMS.Web.csproj

# Run with HTTPS profile
dotnet run --project BODA.VMS.Web/BODA.VMS.Web/BODA.VMS.Web.csproj --launch-profile https

# Watch mode (hot reload)
dotnet watch --project BODA.VMS.Web/BODA.VMS.Web/BODA.VMS.Web.csproj
```

Dev URLs: `https://localhost:7144` (HTTPS) / `http://localhost:5292` (HTTP)

## Solution Structure

```
BODA.VMS.Web.slnx
├── BODA.VMS.Web/BODA.VMS.Web/          # Server — ASP.NET Core 8.0 host
│   ├── Program.cs                       # Service registration, middleware pipeline
│   ├── Components/                      # Server-rendered Razor components (App.razor, Error.razor)
│   └── wwwroot/                         # Server static assets (Bootstrap CSS, favicon)
│
└── BODA.VMS.Web/BODA.VMS.Web.Client/   # Client — Blazor WebAssembly
    ├── Program.cs                       # WASM host builder
    ├── Pages/                           # Routable page components
    ├── Layout/                          # MainLayout.razor, NavMenu.razor
    └── wwwroot/                         # Client static assets
```

## Architecture

**Blazor Web App with InteractiveWebAssembly render mode.** The server project hosts the app shell and serves the WASM client. All interactive UI components run client-side in the browser via WebAssembly.

- **Server project** (`BODA.VMS.Web`): Hosts the Blazor app, will provide RESTful API endpoints for SQLite DB access.
- **Client project** (`BODA.VMS.Web.Client`): All interactive pages and components. Uses `@rendermode InteractiveWebAssembly`.
- **BODA.VMS.ShareLibrary** (.NET Standard 2.0, separate repo): Shared DTOs, enums, constants, and interfaces used across all solutions (Server, Client, Web). New DTOs go here.

## Coding Rules (from ARCHITECTURE.md)

### Cross-framework constraint
The Web project (.NET 8) must NOT reference any .NET Framework DLLs except through ShareLibrary. Only ShareLibrary (.NET Standard 2.0) bridges the two worlds.

### Performance (tact-time sensitive)
- **Image transfer**: Always use JPEG (quality >= 90). Never BMP.
- **Graphics rendering**: Do not draw hundreds of individual Blob boxes. Use inspection region outlines + summary labels to minimize COM overhead.
- **DB writes**: Use `BlockingCollection<T>` with producer-consumer pattern for bulk writes so the vision inspection thread is never blocked.

### Async requirement
All web API and DB I/O operations must use `async/await` (non-blocking).

### SQLite configuration
```sql
PRAGMA journal_mode=WAL;    -- supports 10 concurrent writing clients
PRAGMA foreign_keys = ON;   -- enforce referential integrity
```

### Web UI conventions
- Use MudBlazor-style sidebar layout
- Blazor WASM interactive mode for all UI components

## Key Web Features (planned/in-progress)

- **Production history**: Date-based stats, NG image/data popup on item click
- **Authentication**: Admin-approval account creation with JWT
- **Client management**: 10 vision clients — IP, name, index management, real-time alive check
