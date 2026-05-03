# Issue #38 — Plan: Automate Player Version Publishing & Remove Redundant Default-Playlist

**Issue:** [#38](https://github.com/glensouza/PlainSight/issues/38)
**Branch:** `feature/issue-38-version-automation`
**Author:** plan committed by Claude (Opus 4.7) on 2026-05-03

---

## 1. Goals

1. New Player versions appear in the admin UI **automatically** after a `v*` tag is pushed — no manual upload step in the Blazor UI.
2. The Versions page becomes informational + canary-assignment only.
3. Remove the per-`DeviceGroupVersion` "Default Playlist" control. The Player's idle state is the single fallback when no schedule matches.
4. SHA-256 hash is captured and shown for every ingested binary.

## 2. Background

### What exists today

- `.github/workflows/build-deploy.yml` `build-player` job already publishes `plainsight-player-<tag>.tar.gz` to a GitHub Release on every `v*` tag.
- `Versions.razor` requires an operator to download that asset and re-upload it via `InputFile`, which copies to `UpdatesPath` and inserts a `PlayerVersion` row.
- `UpdateApi.cs` exposes `GET /api/updates/{version}/binary` — Players fetch the binary by version number.
- `PlayerVersion` has `VersionNumber`, `FileName`, `Sha256Hash`, `FileSizeBytes`, `UploadedAt`, `Notes`.
- `DeviceGroupVersion.DefaultPlaylistId` is consumed by `ScheduleService.GetActivePlaylistAsync` as a fallback when no schedule matches.
- `PlainSight.Player.PlaylistService` already returns the idle playlist (`_idlePlaylist`) whenever the main playlist is empty, and `index.html` shows an idle screen when both are empty.

### Why "Default Playlist" is now redundant

Before the idle-state work, "no active schedule" meant "show nothing or the last frame." `DefaultPlaylistId` filled that gap. With idle state in place, the Player handles the empty case gracefully. The setting now adds a competing fallback path that operators have to reason about.

## 3. Design decisions

### Decision A — pull model over push

**Chosen: pull.** PlainSight.Server polls the GitHub Releases REST API on a timer and ingests new releases whose assets include `plainsight-player-*.tar.gz`.

**Rationale:** church deployments are self-hosted and rarely have a public ingress, so a CI→server webhook is brittle. Polling needs only outbound HTTPS to `api.github.com`. A "Refresh from GitHub" button on the Versions page covers the impatient-operator case.

### Decision B — keep `UpdatesPath` on disk

The Player still streams the binary from `GET /api/updates/{version}/binary`, which serves from `UpdatesPath`. We continue to materialize the binary on disk during ingestion; the UpdateApi contract does not change.

### Decision C — extract the GitHub-Releases ingestor as `IPlayerVersionIngestor`

A small interface with one impl (`GitHubReleaseIngestor`) keeps the polling service testable and lets us swap in a different source (e.g. signed S3 manifest) later without rewriting the page or the API.

### Decision D — SHA-256 source of truth

Compute SHA-256 from the downloaded archive **after** extraction of the single-file binary. Store in `PlayerVersion.Sha256Hash`. Display it on the Versions page (truncated, expandable).

### Decision E — drop `DeviceGroupVersion.DefaultPlaylistId`

- Add an EF migration that drops the FK, the index, and the column.
- Remove the fallback branches in `ScheduleService.GetActivePlaylistAsync` (the post-schedule lookups for `DefaultPlaylist`).
- Remove the "Default Playlist" `<select>` from `Versions.razor` and the `playlists` field that backs it.
- Remove `DefaultPlaylistId` from `SaveGroupVersion`/`AddGroup` signatures.

## 4. Task breakdown

Tasks are sized for one model/agent each. Cost-tier suggestions are recommendations — pick whatever you prefer per task. "Gemini CLI" is appropriate for tasks that are mostly mechanical or doc-shaped.

> Note: every task says **explicit types only, no `var`** (project rule).

| # | Task | Suggested tier | Tools |
|---|---|---|---|
| 1 | Plan doc (this file) | Opus 4.7 | — done — |
| 2 | Workflow upgrade: emit `latest.json` manifest + checksum | Haiku 4.5 / Gemini CLI | YAML |
| 3 | Add `IPlayerVersionIngestor` + `GitHubReleaseIngestor` impl | Sonnet 4.6 | C#, EF Core |
| 4 | Hosted background `PlayerVersionPollingService` | Sonnet 4.6 | C#, hosting |
| 5 | Add `POST /api/versions/refresh` admin endpoint | Sonnet 4.6 | C# minimal API |
| 6 | EF migration: drop `DeviceGroupVersion.DefaultPlaylistId` | Sonnet 4.6 | EF migrations |
| 7 | Remove default-playlist branches from `ScheduleService` | Sonnet 4.6 | C# |
| 8 | Versions.razor UI rework (remove upload card, add "Refresh", show hash) | Sonnet 4.6 | Blazor |
| 9 | Update `docs/architecture.md`, `docs/api.md`, `docs/github-actions.md` | Haiku 4.5 / Gemini CLI | docs |
| 10 | Code review (final pass) | Opus 4.7 + `/security-review` | review |

### Task 2 — workflow upgrade

**File:** `.github/workflows/build-deploy.yml`

Acceptance:
- The `build-player` job emits, in addition to the existing tarball:
  - `plainsight-player-<tag>.sha256` containing the hex digest of the tarball.
  - `latest.json` describing `{ version, asset, sha256, sizeBytes, releaseNotesUrl }`. (Optional but nice — lets the ingestor avoid `/releases?per_page=N` rate hits.)
- Job uses `softprops/action-gh-release@v2` with `files:` listing all three.
- No change to existing tag triggers (`refs/tags/v*`).

### Task 3 — `IPlayerVersionIngestor` + `GitHubReleaseIngestor`

**New file:** `src/PlainSight.Server/Services/Versioning/IPlayerVersionIngestor.cs`
**New file:** `src/PlainSight.Server/Services/Versioning/GitHubReleaseIngestor.cs`

Acceptance:
- Interface exposes `Task<int> SyncAsync(CancellationToken ct)` returning the number of new versions ingested.
- Impl uses a typed `HttpClient` (registered with `AddHttpClient<GitHubReleaseIngestor>`) to call `GET /repos/{owner}/{repo}/releases` with `User-Agent: plainsight-server` and an optional `Authorization: Bearer ${GITHUB_TOKEN}` from `IConfiguration["GitHub:Token"]`.
- For each release whose tag starts with `v` and assets include `plainsight-player-<tag>.tar.gz`:
  1. Skip if a `PlayerVersion` with that `VersionNumber` already exists.
  2. Stream the asset to a temp file under `UpdatesPath`, then atomically rename into place as `plainsight-player-<version>`.
  3. Compute SHA-256. If a `.sha256` sidecar asset exists, verify match before commit; on mismatch, delete the file and log a warning.
  4. Insert `PlayerVersion` with version, file name, hash, size, and `Notes = release.Body` (truncated to 1k chars).
- Robust to repeated runs (idempotent), partial downloads (writes to `*.tmp`), and network failures (logs and continues).
- All methods accept and thread `CancellationToken`.
- Configuration keys read: `GitHub:Repository` (owner/repo), `UpdatesPath`, `GitHub:Token` (optional).

### Task 4 — `PlayerVersionPollingService`

**New file:** `src/PlainSight.Server/Services/Versioning/PlayerVersionPollingService.cs`

Acceptance:
- `BackgroundService` registered via `services.AddHostedService<PlayerVersionPollingService>()`.
- Resolves a scoped `IPlayerVersionIngestor` per tick.
- Default interval 30 minutes, configurable via `PlayerVersions:PollInterval` (TimeSpan).
- Calls `SyncAsync` on startup, then on the interval. Logs counts. Catches and logs exceptions per tick (no crash loop).
- Disabled if `PlayerVersions:PollEnabled` is `false`.

### Task 5 — `POST /api/versions/refresh`

**New file:** `src/PlainSight.Server/Api/VersionApi.cs`
**Edit:** `src/PlainSight.Server/Program.cs` to call `MapVersionApi()`.

Acceptance:
- Authenticated (admin only — same policy as other admin endpoints; check existing pattern in `Program.cs`).
- Calls `IPlayerVersionIngestor.SyncAsync` and returns `{ ingested: <int> }`.
- Returns 503 if the ingestor throws, with a generic message; details go to logs.

### Task 6 — EF migration: drop default-playlist column

**New migration:** `dotnet ef migrations add DropGroupDefaultPlaylist --project src/PlainSight.Server`

Acceptance:
- Migration drops FK `FK_DeviceGroupVersions_Playlists_DefaultPlaylistId`, index `IX_DeviceGroupVersions_DefaultPlaylistId`, and column `DefaultPlaylistId`.
- `Down` migration recreates them (allow rollback).
- Update `DeviceGroupVersion` model: remove `DefaultPlaylistId` and `DefaultPlaylist` navigation.
- Update `PlainSightDbContext.OnModelCreating` (and snapshot regenerates automatically).

### Task 7 — `ScheduleService` cleanup

**File:** `src/PlainSight.Server/Services/ScheduleService.cs`

Acceptance:
- Remove the two fallback branches that look up `DefaultPlaylist` on `DeviceGroupVersion`.
- After the schedule lookup returns nothing, return `null` directly.
- The Player consumes `null` as "no playlist → idle state."
- Add an inline-comment WHY-style note above the early `return null` only if the absence of a fallback is non-obvious; otherwise, no comment.

### Task 8 — `Versions.razor` rework

**File:** `src/PlainSight.Server/Components/Pages/Versions.razor`

Acceptance:
- Remove the entire "Upload New Version" card and all of: `uploadVersion`, `uploadNotes`, `selectedFile`, `uploading`, `uploadError`, `OnFileSelected`, `UploadVersion`.
- Replace with a "Sync from GitHub" button that calls the new `POST /api/versions/refresh` and shows the resulting toast / count.
- Add a SHA-256 column (truncated 12 chars, full on hover).
- Remove the "Default Playlist" `<select>` from each group card and the `playlists` field.
- Update workflow note in "Canary Deployment Workflow" card to: "1. Push a `v*` tag → CI builds & publishes → server ingests within 30 min (or click Sync). 2. Assign to test group. 3. ... etc."
- Keep the existing delete-version button (operator may still want to GC).

### Task 9 — docs

Update:
- `docs/architecture.md` — version-flow diagram description.
- `docs/api.md` — new `POST /api/versions/refresh`; remove any note about uploading.
- `docs/github-actions.md` — describe the new `latest.json` and `.sha256` artifacts.

### Task 10 — code review

Run, in this order:
1. `/review` (PR review subagent)
2. `/security-review` (focuses on the new HTTP client + file-write paths)
3. `dotnet build` and `dotnet test` locally (or wait for CI)
4. Smoke test: tag a throwaway `v0.0.0-test`, watch the server log ingest the binary, verify `PlayerVersion` row + file on disk + UpdateApi serves it.
5. Manually verify a Player with no schedule shows the idle screen (regression check for Task 7).

## 5. Risks & mitigations

| Risk | Mitigation |
|---|---|
| GitHub rate-limit on unauthenticated polling | Optional `GitHub:Token` from config; default interval 30 min |
| Half-downloaded binary served to Players | Write to `*.tmp`, fsync, rename; verify hash before insert |
| Schedule-less group goes blank instead of falling back | This is intentional (idle state is the fallback) — call out in PR description |
| Existing operators relying on `DefaultPlaylistId` | Migration loses that data; PR description must say so. Operators add a 24×7 schedule instead |

## 6. Sequencing

Tasks 2, 6, 7 are independent and can land in parallel. Tasks 3, 4, 5 are sequential within themselves. Task 8 depends on 5 and 6. Task 9 depends on the code merging. Task 10 runs last on the assembled PR.

```
2 ─┐
3 ─ 4 ─ 5 ─┐
6 ─ 7 ─────┼─ 8 ─ 9 ─ 10
           │
```
