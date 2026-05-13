using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PlainSight.Server.Data;
using PlainSight.Shared;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services.Versioning;

internal sealed partial class ManifestReconciler : IPlayerVersionReconciler
{
    private readonly PlainSightDbContext dbContext;
    private readonly ILogger<ManifestReconciler> logger;
    private readonly SignatureVerifier verifier;
    private readonly string updatesDir;

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly CanonicalJsonContext CanonicalContext = new(CanonicalOptions);

    public ManifestReconciler(PlainSightDbContext dbContext, IConfiguration configuration, ILogger<ManifestReconciler> logger, SignatureVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(verifier);

        this.dbContext = dbContext;
        this.logger = logger;
        this.verifier = verifier;
        this.updatesDir = MediaPathResolver.Resolve(configuration["UpdatesPath"] ?? Path.Combine(AppContext.BaseDirectory, "Updates"));
    }

    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        if (!Directory.Exists(this.updatesDir))
        {
            this.logger.LogWarning("UpdatesPath {UpdatesPath} does not exist. Skipping reconciliation.", this.updatesDir);
            return 0;
        }

        EnumerationOptions options = new() { IgnoreInaccessible = true };
        string[] manifestFiles = Directory.GetFiles(this.updatesDir, "*.json", options);
        int ingestedCount = 0;

        foreach (string manifestPath in manifestFiles)
        {
            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, ct);
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(json);

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Signature) ||
                    string.IsNullOrWhiteSpace(manifest.FileName) || manifest.FileName != Path.GetFileName(manifest.FileName))
                {
                    this.logger.LogWarning("Manifest {Path} is malformed or contains an invalid FileName. Skipping.", manifestPath);
                    continue;
                }

                bool exists = await this.dbContext.PlayerVersions.AnyAsync(v => v.VersionNumber == manifest.Version, ct);
                if (exists)
                {
                    continue; // Already ingested
                }

                // Compute canonical JSON (without signature, keys sorted alphabetically)
                CanonicalManifest canonicalObj = new()
                {
                    FileName = manifest.FileName,
                    Notes = manifest.Notes,
                    ReleaseUrl = manifest.ReleaseUrl,
                    Sha256 = manifest.Sha256,
                    SignedAt = manifest.SignedAt,
                    SizeBytes = manifest.SizeBytes,
                    Version = manifest.Version
                };

                string canonicalJson = JsonSerializer.Serialize(canonicalObj, CanonicalContext.CanonicalManifest);
                byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonicalJson);

                byte[] signatureBytes = Convert.FromBase64String(manifest.Signature);

                if (!this.verifier.VerifyDer(canonicalBytes, signatureBytes))
                {
                    this.logger.LogWarning("Signature verification failed for manifest {Path}. Skipping.", manifestPath);
                    continue;
                }

                string binaryPath = Path.Combine(this.updatesDir, manifest.FileName);
                if (!File.Exists(binaryPath))
                {
                    this.logger.LogWarning("Binary {BinaryPath} referenced in manifest {Path} does not exist. Skipping.", binaryPath, manifestPath);
                    continue;
                }

                string actualSha256 = await ComputeSha256Async(binaryPath, ct);
                if (!actualSha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    this.logger.LogWarning("SHA-256 mismatch for binary {BinaryPath}. Expected: {Expected}, Actual: {Actual}. Skipping.", binaryPath, manifest.Sha256, actualSha256);
                    continue;
                }

                DateTime uploadedAt = DateTime.TryParse(manifest.SignedAt, out DateTime dt) ? dt.ToUniversalTime() : DateTime.UtcNow;

                PlayerVersion newVersion = new()
                {
                    VersionNumber = manifest.Version,
                    FileName = manifest.FileName,
                    Sha256Hash = manifest.Sha256,
                    FileSizeBytes = manifest.SizeBytes,
                    UploadedAt = uploadedAt,
                    Notes = manifest.Notes.Length > 1000 ? manifest.Notes.Substring(0, 1000) : manifest.Notes
                };

                this.dbContext.PlayerVersions.Add(newVersion);
                ingestedCount++;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Error processing manifest {Path}. Skipping.", manifestPath);
            }
        }

        if (ingestedCount > 0)
        {
            await this.dbContext.SaveChangesAsync(ct);
            this.logger.LogInformation("Ingested {Count} new player versions.", ingestedCount);
        }

        // Prune DB records whose binary no longer exists on disk.
        List<PlayerVersion> allVersions = await this.dbContext.PlayerVersions.ToListAsync(ct);
        int prunedCount = 0;
        foreach (PlayerVersion v in allVersions)
        {
            string binaryPath = Path.Combine(this.updatesDir, Path.GetFileName(v.FileName));
            if (!File.Exists(binaryPath))
            {
                this.dbContext.PlayerVersions.Remove(v);
                prunedCount++;
                this.logger.LogInformation("Pruned stale version record {Version} — binary not found on disk.", v.VersionNumber);
            }
        }

        if (prunedCount > 0)
        {
            await this.dbContext.SaveChangesAsync(ct);
            this.logger.LogInformation("Pruned {Count} stale player version record(s).", prunedCount);
        }

        if (ingestedCount == 0 && prunedCount == 0)
        {
            this.logger.LogDebug("Reconciliation completed. No changes.");
        }

        return ingestedCount;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using FileStream fs = File.OpenRead(filePath);
        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = await sha256.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // Used for reading the on-disk format
    private sealed class Manifest
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; init; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;

        [JsonPropertyName("signedAt")]
        public string SignedAt { get; init; } = string.Empty;

        [JsonPropertyName("releaseUrl")]
        public string ReleaseUrl { get; init; } = string.Empty;

        [JsonPropertyName("notes")]
        public string Notes { get; init; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; init; } = string.Empty;
    }

    // Used for generating the canonical string. Properties must be ordered alphabetically to match jq --sort-keys.
    private sealed class CanonicalManifest
    {
        [JsonPropertyName("fileName")]
        [JsonPropertyOrder(1)]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("notes")]
        [JsonPropertyOrder(2)]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("releaseUrl")]
        [JsonPropertyOrder(3)]
        public string ReleaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        [JsonPropertyOrder(4)]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("signedAt")]
        [JsonPropertyOrder(5)]
        public string SignedAt { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        [JsonPropertyOrder(6)]
        public long SizeBytes { get; set; }

        [JsonPropertyName("version")]
        [JsonPropertyOrder(7)]
        public string Version { get; set; } = string.Empty;
    }

    [JsonSerializable(typeof(CanonicalManifest))]
    private sealed partial class CanonicalJsonContext : JsonSerializerContext
    {
    }
}
