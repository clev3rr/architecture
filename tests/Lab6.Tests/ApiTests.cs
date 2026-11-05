using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lab6.Client;
using Lab6.Crypto;
using Lab6.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lab6.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient http;
    private readonly MemoryStore store;
    private readonly AesService aes = new();
    private readonly RsaService rsa = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        http = factory.CreateClient();
        store = factory.Services.GetRequiredService<MemoryStore>();
    }

    [Fact]
    public void ServerGeneratesTenKeysAtStartup()
    {
        var isolated = new MemoryStore(rsa);
        Assert.Equal(10, isolated.KeyCount);
        Assert.All(Enumerable.Range(1, 10), id => Assert.NotNull(isolated.GetKey(id)));
    }

    [Fact]
    public async Task GenerateAndReadRsaKey()
    {
        var response = await http.PostAsync("/api/crypto-keys/generate/rsa-keys", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = await response.Content.ReadFromJsonAsync<int>();
        var publicKey = await http.GetFromJsonAsync<string>($"/api/crypto-keys/rsa-public-key/{id}");
        Assert.NotNull(publicKey);
        Assert.NotEmpty(Convert.FromBase64String(publicKey));
    }

    [Fact]
    public async Task UnknownRsaKeyReturns404() => Assert.Equal(HttpStatusCode.NotFound,
        (await http.GetAsync("/api/crypto-keys/rsa-public-key/2147483647")).StatusCode);

    [Fact]
    public async Task RandomKeyCarriesMatchingRsaId()
    {
        var response = await http.GetAsync("/api/crypto-keys/rsa-public-key");
        var id = int.Parse(response.Headers.GetValues("x-rsa-id").Single());
        Assert.Equal(store.GetKey(id)!.PublicKey, await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task ActualClientExchangesSeveralMessagesAndStartsNewSession()
    {
        var client = new SecureChannel(http);
        await client.OpenAsync();
        var firstSession = client.SessionId;
        foreach (var text in new[] { "Привіт, сервере! 🔐", "", new string('я', 16000) })
        {
            var reply = await client.SendAsync(text);
            Assert.Equal(text, reply.Text);
            Assert.Equal(firstSession, reply.SessionId);
            Assert.InRange(reply.ReceivedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.DoesNotContain("Привіт", client.LastCipherText);
        }
        await client.OpenAsync();
        Assert.NotEqual(firstSession, client.SessionId);
        Assert.Equal(1, (await client.SendAsync("Нова сесія")).Sequence);
    }

    [Fact]
    public async Task ClientsHaveIndependentSessions()
    {
        var first = new SecureChannel(http);
        var second = new SecureChannel(http);
        await Task.WhenAll(first.OpenAsync(), second.OpenAsync());
        Assert.NotEqual(first.SessionId, second.SessionId);
        var replies = await Task.WhenAll(first.SendAsync("first"), second.SendAsync("second"));
        Assert.Equal("first", replies[0].Text);
        Assert.Equal("second", replies[1].Text);
    }

    [Fact]
    public async Task MissingHeadersReturn400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/sessions", new CipherRequest("AA=="))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/messages", new CipherRequest("AA=="))).StatusCode);
    }

    [Fact]
    public async Task UnknownRsaIdReturns404DuringHandshake()
    {
        var request = CreateSessionRequest(Guid.NewGuid(), 2147483647, "AA==");
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task SessionHeaderMustMatchEncryptedSessionId()
    {
        var offer = new SessionOffer(Guid.NewGuid(), aes.GenerateSecretKey());
        var encrypted = rsa.Encrypt(store.GetKey(1)!.PublicKey, JsonSerializer.Serialize(offer, Json));
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(CreateSessionRequest(Guid.NewGuid(), 1, encrypted))).StatusCode);
    }

    [Fact]
    public async Task DuplicateSessionReturns409()
    {
        var id = Guid.NewGuid();
        var offer = new SessionOffer(id, aes.GenerateSecretKey());
        var encrypted = rsa.Encrypt(store.GetKey(1)!.PublicKey, JsonSerializer.Serialize(offer, Json));
        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(CreateSessionRequest(id, 1, encrypted))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(CreateSessionRequest(id, 1, encrypted))).StatusCode);
    }

    [Fact]
    public async Task UnknownSessionReturns404() => Assert.Equal(HttpStatusCode.NotFound,
        (await SendPacket(Guid.NewGuid(), "AA==")).StatusCode);

    [Fact]
    public async Task ExpiredSessionReturns410()
    {
        var id = Guid.NewGuid();
        store.AddSession(id, new(aes.GenerateSecretKey(), DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Gone, (await SendPacket(id, "AA==")).StatusCode);
    }

    [Fact]
    public async Task ReplayIsRejectedWithoutBreakingNextMessage()
    {
        var client = new SecureChannel(http);
        await client.OpenAsync();
        await client.SendAsync("first");
        Assert.Equal(HttpStatusCode.Conflict, (await SendPacket(client.SessionId, client.LastCipherText)).StatusCode);
        Assert.Equal(2, (await client.SendAsync("second")).Sequence);
    }

    [Fact]
    public async Task ModifiedPacketIsRejectedWithoutAdvancingSequence()
    {
        var client = new SecureChannel(http);
        await client.OpenAsync();
        await client.SendAsync("first");
        var bytes = Convert.FromBase64String(client.LastCipherText);
        bytes[^1] ^= 1;
        Assert.Equal(HttpStatusCode.BadRequest, (await SendPacket(client.SessionId, Convert.ToBase64String(bytes))).StatusCode);
        Assert.Equal(2, (await client.SendAsync("second")).Sequence);
    }

    [Fact]
    public async Task MalformedJsonReturns400()
    {
        var response = await http.PostAsync("/api/messages", new StringContent("{broken", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizedMessageIsRejected()
    {
        var id = Guid.NewGuid();
        var key = aes.GenerateSecretKey();
        store.AddSession(id, new(key, DateTimeOffset.UtcNow.AddMinutes(1)));
        var text = JsonSerializer.Serialize(new PlainMessage(id, 1, new string('x', 16001)), Json);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendPacket(id, aes.Encrypt(key, text))).StatusCode);
    }

    [Fact]
    public async Task MessageSessionBindingIsChecked()
    {
        var id = Guid.NewGuid();
        var key = aes.GenerateSecretKey();
        store.AddSession(id, new(key, DateTimeOffset.UtcNow.AddMinutes(1)));
        var text = JsonSerializer.Serialize(new PlainMessage(Guid.NewGuid(), 1, "hello"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendPacket(id, aes.Encrypt(key, text))).StatusCode);
    }

    [Fact]
    public async Task OpenApiIncludesAllEndpointsAndNoPrivateKeys()
    {
        var response = await http.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/sessions", out _));
        Assert.True(paths.TryGetProperty("/api/messages", out _));
        Assert.True(paths.TryGetProperty("/api/crypto-keys/generate/rsa-keys", out _));
        Assert.DoesNotContain("privateKey", text, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage CreateSessionRequest(Guid id, int rsaId, string cipher)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions");
        request.Headers.Add("x-rsa-id", rsaId.ToString());
        request.Headers.Add("x-session-id", id.ToString());
        request.Content = JsonContent.Create(new CipherRequest(cipher));
        return request;
    }

    private Task<HttpResponseMessage> SendPacket(Guid id, string cipher)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Headers.Add("x-session-id", id.ToString());
        request.Content = JsonContent.Create(new CipherRequest(cipher));
        return http.SendAsync(request);
    }
}
