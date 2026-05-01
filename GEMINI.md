# PlainSight: Distributed Digital Signage System

PlainSight is an enterprise-grade digital signage solution optimized for Raspberry Pi 5, built with .NET 10. It uses a server-side rendering approach where complex content is rendered to video on the server and streamed to players via SMB, ensuring high reliability and zero-touch maintenance.

## 🚀 Project Overview

- **Server**: ASP.NET Core 10 & Blazor Web App (Interactive Server Mode). Handles device management, content rendering (PuppeteerSharp), and update distribution.
- **Player**: .NET 10 web app that streams content from an SMB share, reports telemetry via heartbeats, supports self-updating, and launches Chromium in kiosk mode to display an HTML5 video player page.
- **Orchestration**: .NET Aspire is used for development-time orchestration of the server, player, and PostgreSQL database.
- **Storage**: PostgreSQL for metadata/telemetry and Samba for content distribution.

## 🛠️ Building and Running

### Development Environment
The easiest way to run the entire system locally is using .NET Aspire:
```powershell
dotnet run --project src/PlainSight.AppHost
```
This will start the Server, Player, and a PostgreSQL container (with PgAdmin).

### Production Server (Docker)
```bash
docker compose up -d
```
Ensure you have configured your `.env` file based on `.env.example`.

### Player Deployment (Raspberry Pi)
Players are typically deployed using the installation scripts in `deployment/raspberry-pi/`.
To build the player binary manually for ARM64:
```powershell
dotnet publish src/PlainSight.Player/PlainSight.Player.csproj -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## 📏 Development Conventions

### C# Coding Rules — hard requirements

- **NEVER use `var`** — always use explicit types. `string name = "..."` not `var name = "..."`. This applies everywhere without exception.
- **C# 14 idioms**: file-scoped namespaces, `ArgumentNullException.ThrowIfNull`, `Async` suffix on all async methods.
- **Least visibility**: prefer `private`/`internal` before `public`. Do not add public interfaces unless required for DI or testing.
- **CancellationToken**: async methods must accept and thread `CancellationToken` where appropriate.
- **No silent catches**: log and rethrow, or return errors explicitly. Never swallow exceptions silently.
- **Minimal diffs**: avoid unrelated formatting changes. Do not reformat code that is not part of the task.
- **EF migrations**: when adding EF Core schema changes, always run `dotnet ef migrations add <Name>` and include the generated migration files. Never modify existing migration files.
- **No comments by default**: only add a comment when the WHY is non-obvious (a hidden constraint, a subtle invariant, a workaround). Never describe what the code does — well-named identifiers do that.

### UI & Styling (Blazor)
- **CSS Isolation**: Always use Blazor CSS isolation (`.razor.css` files). Never use inline `style` attributes or `<style>` tags in `.razor` files.
- **Accessibility**: Ensure high contrast and readable color schemes. Avoid white backgrounds with gray text.

### Architecture & Patterns
- **Heartbeat Pattern**: Players must report status every 30 seconds via the heartbeat API.
- **Self-Updating**: The player is designed to download updates and restart itself (via systemd).
- **Service Discovery**: Use .NET Aspire service discovery (`http://plainsight-server`) for internal communication.

## 📁 Key Directories

- `src/PlainSight.AppHost`: Aspire orchestrator.
- `src/PlainSight.Server`: Admin dashboard and API controllers.
- `src/PlainSight.Player`: Raspberry Pi player (Chromium kiosk, heartbeat, self-update).
- `src/PlainSight.Shared`: Shared models and DTOs.
- `deployment/`: Configuration and install scripts for Raspberry Pi.
- `docs/`: Comprehensive technical documentation.
