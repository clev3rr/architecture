using System.Text;
using Lab6.Client;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
var url = args.Length > 0 ? args[0] : "http://localhost:5080";
if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("Вкажіть HTTP або HTTPS адресу сервера.");
    return 1;
}
using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
var channel = new SecureChannel(http);
Console.WriteLine("LAB6 | RSA-3072 + AES-256-GCM");
Console.WriteLine($"Сервер: {address}");
Console.WriteLine("Введіть повідомлення. /new - нова сесія, /exit - вихід.");
while (true)
{
    Console.Write("> ");
    var text = Console.ReadLine();
    if (text is null || text == "/exit") break;
    if (text == "/new")
    {
        channel = new SecureChannel(http);
        Console.WriteLine("Наступне повідомлення відкриє нову сесію.");
        continue;
    }
    try
    {
        if (channel.SessionId == Guid.Empty || channel.ExpiresAt <= DateTimeOffset.UtcNow)
            await channel.OpenAsync();
        var reply = await channel.SendAsync(text);
        Console.WriteLine($"RSA ID: {channel.RsaId} | Сесія: {reply.SessionId} | Повідомлення: {reply.Sequence}");
        Console.WriteLine($"Шифротекст Base64: {channel.LastCipherText}");
        Console.WriteLine($"Відповідь: {reply.Text}");
        Console.WriteLine($"Отримано сервером: {reply.ReceivedAt:O}");
    }
    catch (Exception error) when (error is HttpRequestException or TaskCanceledException or ArgumentException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Помилка: {error.Message}");
        channel = new SecureChannel(http);
    }
}
return 0;
