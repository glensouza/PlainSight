using System;
using System.IO;
using System.Security.Cryptography;

namespace PlainSight.Server.Services.Versioning;

internal sealed class SignatureVerifier : IDisposable
{
    private readonly ECDsa _ecdsa;

    public SignatureVerifier(string publicKeyPath)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPath);

        _ecdsa = ECDsa.Create();
        string pem = File.ReadAllText(publicKeyPath);
        _ecdsa.ImportFromPem(pem);
    }

    public bool VerifyDer(byte[] data, byte[] signature)
    {
        return _ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public void Dispose()
    {
        _ecdsa.Dispose();
    }
}
