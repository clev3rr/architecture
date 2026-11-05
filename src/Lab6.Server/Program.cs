using System.Security.Cryptography;
using System.Text.Json;
using Lab6.Crypto;
using Lab6.Server;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IRsaService, RsaService>();
builder.Services.AddSingleton<IAesService, AesService>();
builder.Services.AddSingleton<MemoryStore>();
builder.Services.AddOpenApi();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 131072);
var app = builder.Build();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
_ = app.Services.GetRequiredService<MemoryStore>();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException or JsonException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "Некоректні зашифровані дані або параметри запиту." });
    }
});
app.MapOpenApi();
app.MapGet("/", () => Results.Redirect("/openapi/v1.json")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithSummary("Стан сервера");

app.MapPost("/api/crypto-keys/generate/rsa-keys", (IRsaService rsa, MemoryStore store) =>
{
    try { return Results.Ok(store.AddKey(rsa.GenerateCryptoKeys())); }
    catch (CryptographicException) { return Results.BadRequest(new { error = "Не вдалося створити пару RSA." }); }
}).WithSummary("Створити пару RSA і повернути її integer ID")
  .Produces<int>().Produces(400);

app.MapGet("/api/crypto-keys/rsa-public-key/{id:int}", (int id, MemoryStore store) =>
    store.GetKey(id) is { } pair ? Results.Ok(pair.PublicKey) : Results.NotFound())
    .WithSummary("Отримати публічний RSA ключ у Base64").Produces<string>().Produces(404);

app.MapGet("/api/crypto-keys/rsa-public-key", (HttpContext context, MemoryStore store) =>
{
    var item = store.RandomKey();
    context.Response.Headers["x-rsa-id"] = item.Key.ToString();
    return Results.Ok(item.Value.PublicKey);
}).WithSummary("Отримати випадковий публічний ключ і заголовок x-rsa-id").Produces<string>();

app.MapPost("/api/sessions", ([FromHeader(Name = "x-rsa-id")] string rsaIdHeader, [FromHeader(Name = "x-session-id")] string sessionIdHeader, CipherRequest request, IRsaService rsa, MemoryStore store) =>
{
    if (!int.TryParse(rsaIdHeader, out var rsaId) ||
        !Guid.TryParse(sessionIdHeader, out var sessionId) || sessionId == Guid.Empty)
        return Results.BadRequest(new { error = "Потрібні заголовки x-rsa-id та x-session-id." });
    var pair = store.GetKey(rsaId);
    if (pair is null) return Results.NotFound(new { error = "RSA ключ не знайдено." });
    var offer = JsonSerializer.Deserialize<SessionOffer>(rsa.Decrypt(pair.PrivateKey, request.CipherText), json);
    if (offer is null || offer.AesKey is null || offer.SessionId != sessionId)
        return Results.BadRequest(new { error = "Номер сесії не збігається із зашифрованим пакетом." });
    AesService.ValidateKey(offer.AesKey);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
    if (!store.AddSession(sessionId, new(offer.AesKey, expiresAt)))
        return Results.Conflict(new { error = "Такий номер сесії вже існує." });
    return Results.Created($"/api/sessions/{sessionId}", new SessionCreated(sessionId, expiresAt));
}).WithSummary("Передати зашифровані RSA параметри AES")
  .Produces<SessionCreated>(201).Produces(400).Produces(404).Produces(409);

app.MapPost("/api/messages", ([FromHeader(Name = "x-session-id")] string sessionIdHeader, CipherRequest request, IAesService aes, MemoryStore store) =>
{
    if (!Guid.TryParse(sessionIdHeader, out var sessionId))
        return Results.BadRequest(new { error = "Потрібен заголовок x-session-id." });
    var session = store.GetSession(sessionId);
    if (session is null) return Results.NotFound(new { error = "Сесію не знайдено." });
    if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.Json(new { error = "Термін сесії минув." }, statusCode: 410);
    lock (session.SyncRoot)
    {
        var message = JsonSerializer.Deserialize<PlainMessage>(aes.Decrypt(session.Key, request.CipherText), json);
        if (message is null || message.SessionId != sessionId || message.Text is null || message.Text.Length > 16000)
            return Results.BadRequest(new { error = "Некоректне повідомлення або номер сесії." });
        if (message.Sequence != session.LastSequence + 1)
            return Results.Conflict(new { error = "Неправильний порядковий номер повідомлення." });
        var reply = new ServerReply(sessionId, message.Sequence, message.Text, DateTimeOffset.UtcNow);
        var encrypted = aes.Encrypt(session.Key, JsonSerializer.Serialize(reply, json));
        session.LastSequence = message.Sequence;
        return Results.Ok(new CipherRequest(encrypted));
    }
}).WithSummary("Обміняти AES повідомлення й отримати час приймання")
  .Produces<CipherRequest>().Produces(400).Produces(404).Produces(409).Produces(410);

app.Run();
public partial class Program;
