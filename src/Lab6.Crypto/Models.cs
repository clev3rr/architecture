namespace Lab6.Crypto;

public sealed record RsaKeys(string PublicKey, string PrivateKey);
public sealed record AesKey(string Key, string IV);
public sealed record CipherRequest(string CipherText);
public sealed record SessionOffer(Guid SessionId, AesKey AesKey);
public sealed record SessionCreated(Guid SessionId, DateTimeOffset ExpiresAt);
public sealed record PlainMessage(Guid SessionId, long Sequence, string Text);
public sealed record ServerReply(Guid SessionId, long Sequence, string Text, DateTimeOffset ReceivedAt);

public interface IRsaService
{
    RsaKeys GenerateCryptoKeys();
    string Encrypt(string publicKey, string plainText);
    string Decrypt(string privateKey, string cipherText);
}

public interface IAesService
{
    AesKey GenerateSecretKey();
    string Encrypt(AesKey aesKey, string plainText);
    string Decrypt(AesKey aesKey, string cipherText);
}
