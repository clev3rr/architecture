using System.Security.Cryptography;
using System.Text;
using Lab6.Crypto;
using Xunit;

namespace Lab6.Tests;

public sealed class CryptoTests
{
    private static readonly RsaService Rsa = new();
    private static readonly RsaKeys Pair = Rsa.GenerateCryptoKeys();
    private readonly AesService aes = new();

    [Theory]
    [InlineData("")]
    [InlineData("Hello, server!")]
    [InlineData("Привіт, сервере! 🔐\nДругий рядок")]
    public void RsaRoundTrip(string text)
    {
        var encrypted = Rsa.Encrypt(Pair.PublicKey, text);
        Assert.Equal(384, Convert.FromBase64String(encrypted).Length);
        Assert.Equal(text, Rsa.Decrypt(Pair.PrivateKey, encrypted));
    }

    [Fact]
    public void RsaKeysAreImportableAnd3072Bits()
    {
        using var publicKey = RSA.Create();
        publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Pair.PublicKey), out _);
        using var privateKey = RSA.Create();
        privateKey.ImportPkcs8PrivateKey(Convert.FromBase64String(Pair.PrivateKey), out _);
        Assert.Equal(3072, publicKey.KeySize);
        Assert.Equal(publicKey.ExportParameters(false).Modulus, privateKey.ExportParameters(false).Modulus);
    }

    [Fact]
    public void RsaUsesRandomizedPadding() => Assert.NotEqual(
        Rsa.Encrypt(Pair.PublicKey, "same"), Rsa.Encrypt(Pair.PublicKey, "same"));

    [Fact]
    public void RsaRejectsWrongPrivateKey()
    {
        var encrypted = Rsa.Encrypt(Pair.PublicKey, "secret");
        var other = Rsa.GenerateCryptoKeys();
        Assert.Throws<CryptographicException>(() => Rsa.Decrypt(other.PrivateKey, encrypted));
    }

    [Fact]
    public void RsaEnforcesOaepByteLimit()
    {
        Assert.Equal(new string('x', 318), Rsa.Decrypt(Pair.PrivateKey, Rsa.Encrypt(Pair.PublicKey, new string('x', 318))));
        Assert.Throws<ArgumentException>(() => Rsa.Encrypt(Pair.PublicKey, new string('x', 319)));
        Assert.Throws<ArgumentException>(() => Rsa.Encrypt(Pair.PublicKey, new string('я', 160)));
    }

    [Fact]
    public void RsaRejectsMalformedBase64() => Assert.Throws<FormatException>(() => Rsa.Decrypt(Pair.PrivateKey, "%%%"));

    [Theory]
    [InlineData("")]
    [InlineData("Hello, server!")]
    [InlineData("Українська абетка Ґґ Єє Іі Її 🔐\nНовий рядок")]
    public void AesRoundTrip(string text)
    {
        var key = aes.GenerateSecretKey();
        var encrypted = aes.Encrypt(key, text);
        Assert.Equal(Encoding.UTF8.GetByteCount(text) + 28, Convert.FromBase64String(encrypted).Length);
        Assert.Equal(text, aes.Decrypt(key, encrypted));
    }

    [Fact]
    public void AesHandlesLongMessages()
    {
        var text = new string('я', 16000);
        var key = aes.GenerateSecretKey();
        Assert.Equal(text, aes.Decrypt(key, aes.Encrypt(key, text)));
    }

    [Fact]
    public void AesGeneratesIndependentKeysAndIvs()
    {
        var first = aes.GenerateSecretKey();
        var second = aes.GenerateSecretKey();
        Assert.Equal(32, Convert.FromBase64String(first.Key).Length);
        Assert.Equal(12, Convert.FromBase64String(first.IV).Length);
        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(first.IV, second.IV);
    }

    [Fact]
    public void AesDoesNotReuseNonceWithinSession()
    {
        var key = aes.GenerateSecretKey();
        var packets = Enumerable.Range(0, 100).Select(_ => aes.Encrypt(key, "same")).ToArray();
        Assert.Equal(100, packets.Select(p => Convert.ToHexString(Convert.FromBase64String(p).AsSpan(0, 12))).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(28)]
    public void AesRejectsModifiedNonceTagOrCiphertext(int index)
    {
        var key = aes.GenerateSecretKey();
        var bytes = Convert.FromBase64String(aes.Encrypt(key, "secret"));
        bytes[index] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(key, Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void AesRejectsWrongKeyAndIv()
    {
        var key = aes.GenerateSecretKey();
        var other = aes.GenerateSecretKey();
        var encrypted = aes.Encrypt(key, "secret");
        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(other, encrypted));
        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(new(key.Key, other.IV), encrypted));
    }

    [Fact]
    public void AesRejectsInvalidKeySize() => Assert.Throws<ArgumentException>(() => aes.Encrypt(new("AA==", "AA=="), "text"));

    [Theory]
    [InlineData("%%%")] 
    [InlineData("AA==")]
    public void AesRejectsMalformedCiphertext(string ciphertext)
    {
        Assert.ThrowsAny<Exception>(() => aes.Decrypt(aes.GenerateSecretKey(), ciphertext));
    }

    [Fact]
    public void AesPacketInteroperatesWithBclGcm()
    {
        var key = aes.GenerateSecretKey();
        var bytes = Convert.FromBase64String(aes.Encrypt(key, "wire format"));
        var plaintext = new byte[bytes.Length - 28];
        using var gcm = new AesGcm(Convert.FromBase64String(key.Key), 16);
        gcm.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext, Convert.FromBase64String(key.IV));
        Assert.Equal("wire format", Encoding.UTF8.GetString(plaintext));
    }
}
