using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NotchHub.KdeConnect.Protocol;

internal static class CertificateHelper
{
    /// <summary>Self-signed RSA-2048 certificate: CN=deviceId, O=KDE, OU=Kde connect, valid 10 years.</summary>
    public static X509Certificate2 Create(string deviceId)
    {
        using var rsa = RSA.Create(2048);
        var dn = new X500DistinguishedNameBuilder();
        dn.AddOrganizationName("KDE");
        dn.AddOrganizationalUnitName("Kde connect");
        dn.AddCommonName(deviceId);
        var req = new CertificateRequest(dn.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));
        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        var now = DateTimeOffset.UtcNow;
        using var cert = req.Create(dn.Build(), X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            now.AddYears(-1), now.AddYears(10), serial);
        using var withKey = cert.CopyWithPrivateKey(rsa);
        return LoadPfx(withKey.Export(X509ContentType.Pkcs12));
    }

    /// <summary>Imports a PFX with a persisted (non-ephemeral) key, which SChannel needs for TLS.</summary>
    public static X509Certificate2 LoadPfx(byte[] pfx) =>
        X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

    public static string CommonName(X509Certificate2 cert) => cert.GetNameInfo(X509NameType.SimpleName, false);

    public static X509Certificate2 FromRemote(System.Security.Cryptography.X509Certificates.X509Certificate cert) =>
        cert as X509Certificate2 is { } c2 ? X509CertificateLoader.LoadCertificate(c2.RawData) : X509CertificateLoader.LoadCertificate(cert.GetRawCertData());

    /// <summary>
    /// 8-char verification key shown on both devices while pairing: SHA-256 over the two SubjectPublicKeyInfo DERs
    /// (lexicographically larger first) followed by the decimal pairing timestamp; first 8 hex chars, uppercase.
    /// </summary>
    public static string VerificationKey(X509Certificate2 local, X509Certificate2 remote, long timestamp)
    {
        var a = local.PublicKey.ExportSubjectPublicKeyInfo();
        var b = remote.PublicKey.ExportSubjectPublicKeyInfo();
        if (a.AsSpan().SequenceCompareTo(b) < 0) (a, b) = (b, a);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(a);
        sha.AppendData(b);
        if (timestamp != 0) sha.AppendData(Encoding.ASCII.GetBytes(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(sha.GetHashAndReset())[..8].ToUpperInvariant();
    }
}
