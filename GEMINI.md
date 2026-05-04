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

- **NEVER use `var`** — always use explicit types. `string name = "..."` not `var name = "..."`. This applies everywhere without exception, including `foreach` and out-vars.
- **C# 14 idioms**: file-scoped namespaces, `ArgumentNullException.ThrowIfNull`, `Async` suffix on all async methods.
- **Least visibility**: prefer `private`/`internal` before `public`. Do not add public interfaces unless required for DI or testing.
- **CancellationToken**: async methods must accept and thread `CancellationToken` where appropriate.
- **No silent catches**: log and rethrow, or return errors explicitly. Never swallow exceptions silently. Empty catch blocks must use a brace body with a comment explaining what is swallowed (e.g., `catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* expected during shutdown */ }`) — never `catch { }` on one line.
- **EF migrations**: when adding EF Core schema changes, always run `dotnet ef migrations add <Name>` and include the generated migration files. Never modify existing migration files.
- **No comments by default**: only add a comment when the WHY is non-obvious (a hidden constraint, a subtle invariant, a workaround). Never describe what the code does — well-named identifiers do that.

### C# Style Rules — formatting and naming (apply on every edit)

- **`this.` prefix**: prefix all instance field and method access with `this.` (e.g., `this.activeWebSocket`, `this.LoadApiKey()`).
- **No underscore prefix on private fields**: `_logger` → `logger`, `_lock` → `@lock`. Static-readonly and const fields use **PascalCase** (e.g., `CanonicalOptions`, `SmCxvirtualscreen`), not `_camelCase`, not `ALL_CAPS`.
- **Acronym casing**: acronyms longer than two letters use Pascal-style — `OBSDiscoveryService` → `ObsDiscoveryService`, `NDI` stays `Ndi`. Two-letter acronyms (`Id`, `Db`) stay as-is.
- **Primary constructors**: prefer them when no validation is required (`class Foo(IDep dep, ILogger<Foo> logger)`); use a classical constructor when you need `ArgumentNullException.ThrowIfNull`.
- **`init` accessors**: use `init` (not `set`) for properties only set during construction — `Id`, `CreatedAt`, navigation properties, JSON DTO fields, options/manifest types.
- **Computed properties are PascalCase**: e.g., `onlineCount` → `OnlineCount`.
- **No redundant initializers**: drop `= false` on default-bool fields, drop `= null` on reference fields.
- **Collection expressions `[]`** everywhere — instead of `new List<T>()`, `new()`, `Enumerable.Empty<T>()`, or `new[] { ... }`.
- **`coll.Any()` → `coll.Count != 0`** when the collection exposes `Count`.
- **Always brace `if`/`else`/`foreach`/`using`** — never single-line bodies, never single-line early returns. One-statement `if`s still get braces.
- **Switch expressions** over if-else chains that return a value.
- **Pattern matching property patterns**: `device is { NdiAutoSwitch: true, AssignedNdiSourceId: not null }`.
- **Invert conditions to reduce nesting** (early-return / `continue` style).
- **`await using`** for any `IAsyncDisposable` — `FileStream` (`useAsync: true`), `DbContext`, etc.
- **`Lock` (System.Threading.Lock)**, not `object`, for lock targets (.NET 9+). Common names: `@lock` or `processLock`.
- **One top-level type per file**. Split helper types into their own files.
- **Collapse multi-line method signatures to a single line**, even if long.
- **Unused lambda params use `_`**: `(s, e) => ...` → `(_, e) => ...`.
- **`[LibraryImport]` + `partial`** over `[DllImport]` + `extern`.
- **No fully-qualified type names** when a `using` directive can be added.
- **`configuration.GetValue<T>(key, default)` → `configuration.GetValue(key, default)`** when `T` is inferable from the default.

### UI & Styling (Blazor)
- **CSS Isolation**: Always use Blazor CSS isolation (`.razor.css` files). Never use inline `style` attributes or `<style>` tags in `.razor` files.
- **Accessibility**: Ensure high contrast and readable color schemes. Avoid white backgrounds with gray text.
- **`@using` directives** belong in `Components/_Imports.razor`, not on each page. Strip per-page `@using` whenever the namespace can live in `_Imports.razor`.

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
