using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Sodium;

namespace WarpinatorWinClient.App.Interop;

public sealed class WarpCertificateManager
{
    public WarpCertificateManager(LocalMachineProfile profile)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={profile.HostName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddIpAddress(profile.Ipv4Address);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        PublicCertificatePem = certificate.ExportCertificatePem();
        PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
    }

    public string PublicCertificatePem { get; }

    public string PrivateKeyPem { get; }

    public string GetEncodedLocalCertificate(string groupCode)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(groupCode));
        var nonce = SecretBox.GenerateNonce();
        var cipherText = SecretBox.Create(Encoding.UTF8.GetBytes(PublicCertificatePem), nonce, key);
        var payload = new byte[nonce.Length + cipherText.Length];

        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(cipherText, 0, payload, nonce.Length, cipherText.Length);

        return Convert.ToBase64String(payload);
    }

    public bool TryDecryptRemoteCertificate(string encodedCertificate, string groupCode, out string pemCertificate)
    {
        pemCertificate = string.Empty;

        try
        {
            var payload = Convert.FromBase64String(encodedCertificate);
            var nonceLength = SecretBox.GenerateNonce().Length;
            if (payload.Length <= nonceLength)
            {
                return false;
            }

            var nonce = payload[..nonceLength];
            var cipherText = payload[nonceLength..];
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(groupCode));
            var plainText = SecretBox.Open(cipherText, nonce, key);
            pemCertificate = Encoding.UTF8.GetString(plainText);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
