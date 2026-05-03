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

### Decision A — self-hosted runner pushes to the share; server reconciles

**Chosen: self-hosted-runner push, filesystem reconciliation, optional manual refresh.**

The existing `deploy-to-server` job already runs on `runs-on: self-hosted`. The `build-player` job moves to the same runner so it can write directly to the network share that backs `UpdatesPath`. After publishing, the runner also drops a signed manifest JSON next to the binary. The server runs a lightweight reconciler (every 60s plus on startup) that scans `UpdatesPath` for manifests with no matching `PlayerVersion` row and ingests them. A "Sync now" button on the Versions page invokes the same reconciler on demand.

**Rationale:**
- No GitHub PAT lives on the server.
- No outbound dependency from the server to `api.github.com`.
- Filesystem is the source of truth; if the server is down at push time, it self-heals on next reconcile tick.
- An operator can manually drop `<binary> + <manifest>` into the share for emergency recovery without rebuilding from CI.
- The GitHub Release publish step is **kept** (Players or operators can still grab the tarball from GitHub for cold-bootstrapping a new site whose share isn't yet populated), but it's no longer load-bearing — the server never reads it.

### Decision B — keep `UpdatesPath` on disk

The Player still streams the binary from `GET /api/updates/{version}/binary`, which serves from `UpdatesPath`. The reconciler writes into that same directory; the UpdateApi contract does not change.

### Decision C — `IPlayerVersionReconciler` abstraction

Single impl (`ManifestReconciler`) reads the filesystem. The interface keeps the hosted service and the API endpoint testable and leaves room for a future alternative source without rewriting callers.

### Decision D — SHA-256 source of truth

The runner writes the SHA-256 into the manifest JSON and signs the manifest. The reconciler:
1. Verifies the manifest signature (Decision F).
2. Computes SHA-256 of the binary on disk and compares to the manifest.
3. Only inserts the `PlayerVersion` row if both checks pass.

The hash is displayed on the Versions page (truncated to 12 chars, full on hover).

### Decision E — drop `DeviceGroupVersion.DefaultPlaylistId`

- Add an EF migration that drops the FK, the index, and the column.
- Remove the fallback branches in `ScheduleService.GetActivePlaylistAsync` (the post-schedule lookups for `DefaultPlaylist`).
- Remove the "Default Playlist" `<select>` from `Versions.razor` and the `playlists` field that backs it.
- Remove `DefaultPlaylistId` from `SaveGroupVersion`/`AddGroup` signatures.

### Decision F — ECDSA P-256 manifest signing

**Chosen: ECDSA P-256 (asymmetric), signing the canonical-JSON manifest.**

- One keypair generated offline by the maintainer.
- Private key lives only as a GitHub Actions secret (`secrets.PLAINSIGHT_SIGNING_KEY`, PEM PKCS8).
- Public key committed at `src/PlainSight.Server/Keys/release-signing.pub` (PEM SubjectPublicKeyInfo) and loaded once at startup.
- Manifest JSON shape:
  ```json
  {
    "version": "1.2.0",
    "fileName": "plainsight-player-1.2.0",
    "sizeBytes": 73452112,
    "sha256": "abc123…",
    "signedAt": "2026-05-03T14:22:00Z",
    "releaseUrl": "https://github.com/glensouza/PlainSight/releases/tag/v1.2.0",
    "notes": "…",
    "signature": "<base64-DER ECDSA-SHA256 over canonical JSON without the signature field>"
  }
  ```
- Server verifies via `ECDsa.Create()` + `VerifyData(...)` (BCL native — no NuGet additions).
- On signature failure: log a warning, refuse to ingest, leave the offending manifest in place for operator inspection.

**Why ECDSA over alternatives:**
- vs **HMAC**: asymmetric — server compromise can't forge new versions. The share is mounted on the server, so this matters.
- vs **GPG**: no key-server dance, no `gpg` toolchain on Windows dev boxes.
- vs **cosign**: simpler — no Sigstore transparency-log dependency for an internal trust loop.

**Threat covered:** an attacker with write access to the share drops a malicious binary. They can't sign a matching manifest without the runner's private key, so the server refuses to ingest it; existing rows are unaffected (they were verified at ingest).

**Threat NOT covered:** a compromised runner. Same blast radius as a compromised CI today, so no regression.

## 4. Task breakdown

Tasks are sized for one model/agent each. Cost-tier suggestions are recommendations — pick whatever you prefer per task. "Gemini CLI" is appropriate for tasks that are mostly mechanical or doc-shaped (no compile loop required).

> Note: every task says **explicit types only, no `var`** (project rule).

| # | Task | Suggested tier | Tools |
|---|---|---|---|
| 1 | Plan doc (this file) | Opus 4.7 | — done — |
| 2 | Workflow upgrade: move `build-player` to self-hosted; sign manifest; drop on share | Sonnet 4.6 | YAML, openssl |
| 3 | `IPlayerVersionReconciler` + `ManifestReconciler` (filesystem + signature verify) | Sonnet 4.6 | C# |
| 4 | Hosted `ReconciliationBackgroundService` (60s tick + startup tick) | Sonnet 4.6 | C# hosting |
| 5 | `POST /api/versions/refresh` admin endpoint | Sonnet 4.6 | C# minimal API |
| 6 | EF migration: drop `DeviceGroupVersion.DefaultPlaylistId` | Sonnet 4.6 | EF migrations |
| 7 | Remove default-playlist branches from `ScheduleService` | Sonnet 4.6 / Haiku 4.5 | C# |
| 8 | `Versions.razor` rework (remove upload card; add "Sync now"; show hash) | Sonnet 4.6 | Blazor |
| 9 | Update `docs/architecture.md`, `docs/api.md`, `docs/github-actions.md` | Haiku 4.5 / Gemini CLI | docs |
| 10 | Code review (final pass) | Opus 4.7 + `/review` + `/security-review` | review |

### Task 2 — workflow upgrade

**File:** `.github/workflows/build-deploy.yml`

Acceptance:
- The `build-player` job moves from `runs-on: ubuntu-latest` to `runs-on: self-hosted`.
- After `dotnet publish` and creating the tar.gz, the runner additionally writes the **single-file binary** (not the tarball) to the share at `${{ vars.UPDATES_PATH }}/plainsight-player-<version>.tmp`, then renames to `plainsight-player-<version>` (atomic on local FS).
- Computes SHA-256 of that binary.
- Builds the canonical-JSON manifest (sorted keys, no whitespace) **without** the `signature` field, signs it with `openssl dgst -sha256 -sign <(printf '%s' "$PLAINSIGHT_SIGNING_KEY")`, base64-encodes, then re-emits the manifest with the `signature` field appended. Writes it as `plainsight-player-<version>.json` next to the binary.
- The existing GitHub Release publish step is preserved (tarball + manifest attached as release assets) for cold-bootstrap use.
- Reads `secrets.PLAINSIGHT_SIGNING_KEY` (PEM PKCS8). Reads `vars.UPDATES_PATH` for the share path.
- No change to existing tag trigger (`refs/tags/v*`).
- Documents how to generate the keypair in a code comment at the top of the job.

### Task 3 — `IPlayerVersionReconciler` + `ManifestReconciler`

**New file:** `src/PlainSight.Server/Services/Versioning/IPlayerVersionReconciler.cs`
**New file:** `src/PlainSight.Server/Services/Versioning/ManifestReconciler.cs`
**New file:** `src/PlainSight.Server/Services/Versioning/SignatureVerifier.cs`
**New file:** `src/PlainSight.Server/Keys/release-signing.pub` (committed PEM, public key only)

Acceptance:
- Interface: `Task<int> ReconcileAsync(CancellationToken ct)` returns count of newly ingested versions.
- Implementation steps:
  1. List `*.json` files in `UpdatesPath`.
  2. For each, deserialize the manifest. If a `PlayerVersion` row already exists for `manifest.version`, skip.
  3. Recompute the canonical-JSON form **without** the `signature` field. Verify the base64 signature via `SignatureVerifier.Verify(canonical, signature)`. On failure: log warning, skip, do not delete.
  4. Confirm `manifest.fileName` exists in `UpdatesPath`. Compute SHA-256 of that file. Compare to `manifest.sha256`. On mismatch: log warning, skip.
  5. Insert `PlayerVersion { VersionNumber, FileName, Sha256Hash, FileSizeBytes, UploadedAt = manifest.signedAt, Notes = manifest.notes }`.
- `SignatureVerifier`:
  - Loads the PEM public key once at startup (`ECDsa.Create(); ImportSubjectPublicKeyInfo(...)`).
  - `Verify(byte[] data, byte[] signature)` returns `bool`.
  - Path of the PEM is read from `IConfiguration["PlayerVersions:PublicKeyPath"]` with a default of the embedded committed file.
- Canonical JSON: implement a small `CanonicalJson` helper that sorts keys and emits without whitespace. Document the algorithm in a comment.
- All methods accept and thread `CancellationToken`.
- Robust to partial files (a `.json` whose binary peer is missing): log and skip.
- Robust to malformed JSON: log and skip.

### Task 4 — `ReconciliationBackgroundService`

**New file:** `src/PlainSight.Server/Services/Versioning/ReconciliationBackgroundService.cs`

Acceptance:
- `BackgroundService` registered via `services.AddHostedService<ReconciliationBackgroundService>()`.
- Resolves a scoped `IPlayerVersionReconciler` per tick.
- Runs once on startup, then every `PlayerVersions:ReconcileInterval` (TimeSpan, default 60 seconds).
- Logs the count when > 0; silent at debug level when 0.
- Catches per-tick exceptions and logs at warning; never crashes the host.
- Disabled if `PlayerVersions:ReconcileEnabled` is `false`.

### Task 5 — `POST /api/versions/refresh`

**New file:** `src/PlainSight.Server/Api/VersionApi.cs`
**Edit:** `src/PlainSight.Server/Program.cs` to call `MapVersionApi()` and to register the reconciler + hosted service.

Acceptance:
- Authenticated (admin only — match the policy used by other admin endpoints; check existing pattern in `Program.cs`).
- Calls `IPlayerVersionReconciler.ReconcileAsync` once and returns `{ ingested: <int> }`.
- Returns 503 if the reconciler throws; details go to logs only.

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
- Replace with a "Sync now" button that calls `POST /api/versions/refresh` and shows the resulting toast / ingested count.
- Add a SHA-256 column to the version list (truncated 12 chars, full on hover via `title` attribute).
- Remove the "Default Playlist" `<select>` from each group card and the `playlists` field.
- Update the "Canary Deployment Workflow" card text to:
  1. Push a `v*` tag → CI builds, signs the manifest, drops binary + manifest on the share.
  2. Server reconciles within 60 s (or click **Sync now**).
  3. Assign the new version to a small test group.
  4. Monitor; use Screenshot to verify.
  5. Assign to "Default" → all unassigned groups follow.
  6. Devices auto-update within 30–60 s via heartbeat.
- Keep the existing delete-version button (operator may still want to GC; delete should remove the binary AND its `.json` manifest from `UpdatesPath`).

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
| Half-written binary picked up mid-write | Runner writes `<file>.tmp` then renames; reconciler only reads files whose `.json` peer exists |
| Manifest signature forged by share-write attacker | Asymmetric signing (Decision F); private key never on server |
| Reconciler thrashes on a permanently bad manifest | Per-file warning is logged once per process tick; consider a small in-memory `failedAt` set if it gets noisy |
| Self-hosted runner offline at tag time | Existing failure mode (deploy job already depends on it); release stays in GitHub for manual recovery |
| Public key file is missing or malformed at startup | Fail fast with a clear log; reconciler refuses to run until fixed |
| Schedule-less group goes blank | Intentional — idle state is the fallback; called out in PR description |
| Operators rely on `DefaultPlaylistId` today | Migration loses that data; PR description must say so. Operators replace with a 24×7 schedule |

## 6. Sequencing

Tasks 2, 6, 7 are independent and can land in parallel. Task 3 depends on the manifest format from Task 2. Tasks 4 and 5 depend on Task 3. Task 8 depends on Tasks 5 and 6. Task 9 depends on code stabilising. Task 10 runs last.

```
2 ─ 3 ─ 4 ──┐
       └ 5 ─┤
6 ─ 7 ──────┼─ 8 ─ 9 ─ 10
            │
```

## 7. Orchestration plan

Two ways to execute the breakdown:

### 7a. User-driven (one-task-per-session)

Open a new chat for each task, set the model in `/model`, paste the task block from §4 plus the reference to this plan file, and run. This gives you full control over per-task model selection and keeps each session's context tight.

**Best for:** tasks 3, 4, 5, 8 (substantive C# / Blazor work where a focused session beats orchestration overhead).

### 7b. Claude-orchestrated (this session spawns sub-agents)

This Opus 4.7 session can spawn sub-agents at lower tiers via the `Agent` tool, and shell out to `gemini` for doc-shaped work:

| Mechanism | Use for | Notes |
|---|---|---|
| `Agent(model: "haiku")` | Tasks 7, 9 | Cheap; spawned fresh — needs a self-contained brief |
| `Agent(model: "sonnet")` | Tasks 2, 6 | Independent; safe to run in parallel |
| Direct work in this session | Tasks 3 → 4 → 5 → 8 | Sequential code chain; orchestration overhead would offset Sonnet savings |
| `gemini` CLI via Bash | Task 9 (docs only) | Runs outside Claude's tool-loop — does not see this conversation. Brief it via the plan file path |
| `/review` + `/security-review` (this session) | Task 10 | Reviews the assembled diff |

**Honest tradeoffs:**
- Sub-agents start cold and re-derive context. For a tightly scoped task with explicit file paths, that's fine. For "read these 5 files, edit, build, fix errors," a single in-session run on the right model is cheaper than Opus-driving-Sonnet-driving-edits.
- `gemini` writes files directly without going through Claude's tool-use loop — fine for docs, risky for C# that has to compile against the codebase. Recommend Gemini for Task 9 only.
- Sub-agents can't update the parent's task list; progress tracking stays here.

**Recommended split:** §7a for the C# core (3, 4, 5, 8), §7b for the parallel-friendly edges (2, 6, 7, 9), me for review (10).
