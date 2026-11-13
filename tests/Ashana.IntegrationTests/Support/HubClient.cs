using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Ashana.IntegrationTests.Support;

/// <summary>
/// A tiny SignalR client speaking the JSON hub protocol directly over a WebSocket
/// (negotiate → connect → handshake → invoke / receive). Writing it out shows exactly what
/// the TV board's JavaScript client does on the wire.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ClientWebSocket _socket;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _buffer = new();
    private readonly Queue<string> _messages = new();
    private int _invocationId;

    private HubClient(ClientWebSocket socket) => _socket = socket;

    public static async Task<HubClient> ConnectAsync(Uri baseAddress, string path = "/hubs/orders")
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        // 1. Negotiate over HTTP: the server hands out a connection token and lists its transports.
        using var http = new HttpClient { BaseAddress = baseAddress };
        using var negotiate = await http.PostAsync(new Uri($"{path}/negotiate?negotiateVersion=1", UriKind.Relative), null);
        negotiate.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("connectionToken").GetString();

        // 2. Open the WebSocket and agree on the JSON protocol.
        var socket = new ClientWebSocket();
        var address = new UriBuilder(baseAddress) { Scheme = "ws", Path = path, Query = "id=" + Uri.EscapeDataString(token!) }.Uri;
        await socket.ConnectAsync(address, CancellationToken.None);

        var client = new HubClient(socket);
        await client.SendAsync("""{"protocol":"json","version":1}""");
        var handshake = await client.ReceiveAsync();
        if (handshake != "{}")
        {
            throw new InvalidOperationException("SignalR handshake failed: " + handshake);
        }

        return client;
    }

    /// <summary>Calls a hub method and returns its result.</summary>
    public async Task<JsonDocument> InvokeAsync(string target)
    {
        var id = (++_invocationId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await SendAsync($$"""{"type":1,"invocationId":"{{id}}","target":"{{target}}","arguments":[]}""");

        using var completion = await WaitForAsync(m => m.GetProperty("type").GetInt32() == 3 && m.GetProperty("invocationId").GetString() == id);
        if (completion.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"{target} failed: {error.GetString()}");
        }

        return JsonDocument.Parse(completion.RootElement.GetProperty("result").GetRawText());
    }

    /// <summary>Waits for the server to call <paramref name="target"/> on this client and returns the message.</summary>
    public Task<JsonDocument> WaitForCallAsync(string target) =>
        WaitForAsync(m => m.GetProperty("type").GetInt32() == 1 && m.GetProperty("target").GetString() == target);

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        _socket.Dispose();
    }

    private async Task<JsonDocument> WaitForAsync(Func<JsonElement, bool> match)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (true)
        {
            var document = JsonDocument.Parse(await ReceiveAsync(timeout.Token));
            if (match(document.RootElement))
            {
                return document;
            }

            document.Dispose(); // pings (type 6) and other messages
        }
    }

    private Task SendAsync(string message) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(message + RecordSeparator), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    private async Task<string> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var bytes = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        while (_messages.Count == 0)
        {
            var result = await _socket.ReceiveAsync(bytes, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("The server closed the connection.");
            }

            var count = _decoder.GetChars(bytes, 0, result.Count, chars, 0);
            _buffer.Append(chars, 0, count);

            var text = _buffer.ToString();
            var last = text.LastIndexOf(RecordSeparator);
            if (last >= 0)
            {
                foreach (var message in text[..last].Split(RecordSeparator))
                {
                    _messages.Enqueue(message);
                }

                _buffer.Clear().Append(text[(last + 1)..]);
            }
        }

        return _messages.Dequeue();
    }
}
