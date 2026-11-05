using System.Security.Cryptography;
using System.Text;

namespace Lab6.Crypto;

public sealed class AesService : IAesService
{
    public AesKey GenerateSecretKey() => new(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)));

    public static void ValidateKey(AesKey key)
    {
        if (Convert.FromBase64String(key.Key).Length != 32 || Convert.FromBase64String(key.IV).Length != 12)
            throw new ArgumentException("Потрібні 32 байти ключа та 12 байтів IV.");
    }

    public string Encrypt(AesKey aesKey, string plainText)
    {
        ValidateKey(aesKey);
        ArgumentNullException.ThrowIfNull(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var seed = Convert.FromBase64String(aesKey.IV);
        for (var i = 0; i < nonce.Length; i++) nonce[i] ^= seed[i];
        var data = Encoding.UTF8.GetBytes(plainText);
        var result = new byte[28 + data.Length];
        nonce.CopyTo(result, 0);
        using var aes = new AesGcm(Convert.FromBase64String(aesKey.Key), 16);
        aes.Encrypt(nonce, data, result.AsSpan(28), result.AsSpan(12, 16), seed);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(AesKey aesKey, string cipherText)
    {
        ValidateKey(aesKey);
        var data = Convert.FromBase64String(cipherText);
        if (data.Length < 28) throw new CryptographicException("Закороткий шифротекст.");
        var result = new byte[data.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(aesKey.Key), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), result,
            Convert.FromBase64String(aesKey.IV));
        return Encoding.UTF8.GetString(result);
    }
}
