using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlainSight.Server.Data;
using PlainSight.Shared.Models;

namespace PlainSight.Server.Services.Versioning;

internal sealed partial class ManifestReconciler : IPlayerVersionReconciler
{
    private readonly PlainSightDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManifestReconciler> _logger;
    private readonly string _updatesPath;
    private readonly string _publicKeyPath;

    public ManifestReconciler(
        PlainSightDbContext dbContext,
        IConfiguration configuration,
        ILogger<ManifestReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        _updatesPath = _configuration["PlayerVersions:UpdatesPath"] ?? Path.Combine(AppContext.BaseDirectory, "Updates");
        _publicKeyPath = _configuration["PlayerVersions:PublicKeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "Keys", "release-signing.pub");
    }

    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_updatesPath))
        {
            _logger.LogWarning("UpdatesPath {UpdatesPath} does not exist. Skipping reconciliation.", _updatesPath);
            return 0;
        }

        if (!File.Exists(_publicKeyPath))
        {
            _logger.LogError("Public key {PublicKeyPath} not found. Refusing to run reconciliation.", _publicKeyPath);
            return 0;
        }

        string[] manifestFiles = Directory.GetFiles(_updatesPath, "*.json");
        int ingestedCount = 0;

        using SignatureVerifier verifier = new SignatureVerifier(_publicKeyPath);

        foreach (string manifestPath in manifestFiles)
        {
            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, ct);
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(json);

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Signature) ||
                    string.IsNullOrWhiteSpace(manifest.FileName) || manifest.FileName != Path.GetFileName(manifest.FileName))
                {
                    _logger.LogWarning("Manifest {Path} is malformed or contains an invalid FileName. Skipping.", manifestPath);
                    continue;
                }

                bool exists = await _dbContext.PlayerVersions.AnyAsync(v => v.VersionNumber == manifest.Version, ct);
                if (exists)
                {
                    continue; // Already ingested
                }

                // Compute canonical JSON (without signature, keys sorted alphabetically)
                CanonicalManifest canonicalObj = new CanonicalManifest
                {
                    FileName = manifest.FileName,
                    Notes = manifest.Notes,
                    ReleaseUrl = manifest.ReleaseUrl,
                    Sha256 = manifest.Sha256,
                    SignedAt = manifest.SignedAt,
                    SizeBytes = manifest.SizeBytes,
                    Version = manifest.Version
                };

                string canonicalJson = JsonSerializer.Serialize(canonicalObj, CanonicalJsonContext.Default.CanonicalManifest);
                byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonicalJson);

                byte[] signatureBytes = Convert.FromBase64String(manifest.Signature);

                if (!verifier.VerifyDer(canonicalBytes, signatureBytes))
                {
                    _logger.LogWarning("Signature verification failed for manifest {Path}. Skipping.", manifestPath);
                    continue;
                }

                string binaryPath = Path.Combine(_updatesPath, manifest.FileName);
                if (!File.Exists(binaryPath))
                {
                    _logger.LogWarning("Binary {BinaryPath} referenced in manifest {Path} does not exist. Skipping.", binaryPath, manifestPath);
                    continue;
                }

                string actualSha256 = await ComputeSha256Async(binaryPath, ct);
                if (!actualSha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("SHA-256 mismatch for binary {BinaryPath}. Expected: {Expected}, Actual: {Actual}. Skipping.", binaryPath, manifest.Sha256, actualSha256);
                    continue;
                }

                PlayerVersion newVersion = new PlayerVersion
                {
                    VersionNumber = manifest.Version,
                    FileName = manifest.FileName,
                    Sha256Hash = manifest.Sha256,
                    FileSizeBytes = manifest.SizeBytes,
                    UploadedAt = manifest.SignedAt,
                    Notes = manifest.Notes
                };

                _dbContext.PlayerVersions.Add(newVersion);
                ingestedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing manifest {Path}. Skipping.", manifestPath);
            }
        }

        if (ingestedCount > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Ingested {Count} new player versions.", ingestedCount);
        }
        else
        {
            _logger.LogDebug("Reconciliation completed. No new versions ingested.");
        }

        return ingestedCount;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using FileStream fs = File.OpenRead(filePath);
        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = await sha256.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // Used for reading the on-disk format
    private sealed class Manifest
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("signedAt")]
        public DateTime SignedAt { get; set; }

        [JsonPropertyName("releaseUrl")]
        public string ReleaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;
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
        public DateTime SignedAt { get; set; }

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
