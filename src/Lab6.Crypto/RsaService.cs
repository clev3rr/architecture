using System.Security.Cryptography;
using System.Text;

namespace Lab6.Crypto;

public sealed class RsaService : IRsaService
{
    public RsaKeys GenerateCryptoKeys()
    {
        using var rsa = RSA.Create(3072);
        return new(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
    }

    public string Encrypt(string publicKey, string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        var data = Encoding.UTF8.GetBytes(plainText);
        if (data.Length > rsa.KeySize / 8 - 66)
            throw new ArgumentException("Повідомлення перевищує межу RSA OAEP SHA-256.", nameof(plainText));
        return Convert.ToBase64String(rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256));
    }

    public string Decrypt(string privateKey, string cipherText)
    {
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(cipherText), RSAEncryptionPadding.OaepSHA256));
    }
}
