using System.Net.Http.Json;
using System.Text.Json;
using Lab6.Crypto;

namespace Lab6.Client;

public sealed class SecureChannel(HttpClient http)
{
    private readonly IRsaService rsa = new RsaService();
    private readonly IAesService aes = new AesService();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private AesKey? key;
    private long sequence;
    public Guid SessionId { get; private set; }
    public int RsaId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string LastCipherText { get; private set; } = "";

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        using var publicResponse = await http.GetAsync("api/crypto-keys/rsa-public-key", cancellationToken);
        publicResponse.EnsureSuccessStatusCode();
        var publicKey = await publicResponse.Content.ReadFromJsonAsync<string>(cancellationToken) ?? throw new InvalidOperationException("Сервер не повернув ключ.");
        var rsaId = int.Parse(publicResponse.Headers.GetValues("x-rsa-id").Single());
        var sessionId = Guid.NewGuid();
        var newKey = aes.GenerateSecretKey();
        var offer = new SessionOffer(sessionId, newKey);
        var encrypted = rsa.Encrypt(publicKey, JsonSerializer.Serialize(offer, json));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions");
        request.Headers.Add("x-rsa-id", rsaId.ToString());
        request.Headers.Add("x-session-id", sessionId.ToString());
        request.Content = JsonContent.Create(new CipherRequest(encrypted));
        using var response = await http.SendAsync(request, cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<SessionCreated>(cancellationToken) ?? throw new InvalidOperationException("Немає підтвердження сесії.");
        if (created.SessionId != sessionId) throw new InvalidOperationException("Сервер повернув іншу сесію.");
        SessionId = sessionId;
        RsaId = rsaId;
        ExpiresAt = created.ExpiresAt;
        key = newKey;
        sequence = 0;
    }

    public async Task<ServerReply> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (key is null) throw new InvalidOperationException("Спочатку відкрийте сесію.");
        if (text.Length > 16000) throw new ArgumentException("Максимальна довжина повідомлення - 16000 символів.");
        var next = sequence + 1;
        LastCipherText = aes.Encrypt(key, JsonSerializer.Serialize(new PlainMessage(SessionId, next, text), json));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/messages");
        request.Headers.Add("x-session-id", SessionId.ToString());
        request.Content = JsonContent.Create(new CipherRequest(LastCipherText));
        using var response = await http.SendAsync(request, cancellationToken);
        await CheckResponseAsync(response, cancellationToken);
        var encrypted = await response.Content.ReadFromJsonAsync<CipherRequest>(cancellationToken) ?? throw new InvalidOperationException("Порожня відповідь.");
        var reply = JsonSerializer.Deserialize<ServerReply>(aes.Decrypt(key, encrypted.CipherText), json) ?? throw new InvalidOperationException("Некоректна відповідь.");
        if (reply.SessionId != SessionId || reply.Sequence != next)
            throw new InvalidOperationException("Відповідь належить іншій сесії або повідомленню.");
        sequence = next;
        return reply;
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }
}
