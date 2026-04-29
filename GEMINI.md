# PlainSight: Distributed Digital Signage System

PlainSight is an enterprise-grade digital signage solution optimized for Raspberry Pi 5, built with .NET 10. It uses a server-side rendering approach where complex content is rendered to video on the server and streamed to players via SMB, ensuring high reliability and zero-touch maintenance.

## 🚀 Project Overview

- **Server**: ASP.NET Core 10 & Blazor Web App (Interactive Server Mode). Handles device management, content rendering (PuppeteerSharp), and update distribution.
- **Player**: .NET 10 worker service that streams content from an SMB share, reports telemetry via heartbeats, and supports self-updating.
- **Photino Player**: An alternative UI-based player using Photino.NET for native video playback.
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
dotnet publish src/Signage.Player/Signage.Player.csproj -r linux-arm64 --self-contained -p:PublishSingleFile=true
```

## 📏 Development Conventions

### Code Style
- **Explicit Types**: Always use explicit types instead of `var` in C# code (e.g., `string name = "..."` instead of `var name = "..."`).
- **Modern .NET**: Utilize .NET 10 features and C# 13 syntax where applicable.

### UI & Styling (Blazor)
- **CSS Isolation**: Always use Blazor CSS isolation (`.razor.css` files). Never use inline `style` attributes or `<style>` tags in `.razor` files.
- **Accessibility**: Ensure high contrast and readable color schemes. Avoid white backgrounds with gray text.

### Architecture & Patterns
- **Heartbeat Pattern**: Players must report status every 30 seconds via the heartbeat API.
- **Self-Updating**: The player is designed to download updates and restart itself (via systemd).
- **Service Discovery**: Use .NET Aspire service discovery (`http://signage-server`) for internal communication.

## 📁 Key Directories

- `src/PlainSight.AppHost`: Aspire orchestrator.
- `src/Signage.Server`: Admin dashboard and API controllers.
- `src/Signage.Player`: Core player logic (Console/Worker).
- `src/Signage.Player.Photino`: UI-enhanced player (Photino.NET).
- `src/Signage.Shared`: Shared models and DTOs.
- `deployment/`: Configuration and install scripts for Raspberry Pi.
- `docs/`: Comprehensive technical documentation.
