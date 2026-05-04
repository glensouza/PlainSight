using System.Security.Cryptography;

namespace PlainSight.Server.Services.Versioning;

internal sealed class SignatureVerifier : IDisposable
{
    private readonly ECDsa ecdsa;
    private readonly bool isInitialized;

    public SignatureVerifier(string publicKeyPath)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPath);

        this.ecdsa = ECDsa.Create();
        if (!File.Exists(publicKeyPath))
        {
            return;
        }

        string pem = File.ReadAllText(publicKeyPath);
        this.ecdsa.ImportFromPem(pem);
        this.isInitialized = true;
    }

    public bool VerifyDer(byte[] data, byte[] signature)
    {
        return this.isInitialized && this.ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public void Dispose()
    {
        this.ecdsa.Dispose();
    }
}
