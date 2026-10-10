using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Griff.Agent;

/// <summary>
/// Serves an <see cref="McpServer"/> over MCP's Streamable HTTP transport at <c>http://127.0.0.1:{port}/mcp</c>: each
/// JSON-RPC message is POSTed and answered with plain JSON (no event stream). It listens on the loopback address only and
/// turns away web pages (any Origin that isn't localhost), so only programs on this machine can reach it.
/// </summary>
public sealed class McpHttpTransport : IAsyncDisposable
{
    public const int DefaultPort = 7650;

    private const int MaxBodyBytes = 64 * 1024 * 1024;

    private readonly McpServer _server;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _loop;

    public McpHttpTransport(McpServer server, int port = DefaultPort)
    {
        _server = server;
        Port = port;
    }

    /// <summary>The port asked for, or once started, the one listened on (0 asks for any free port).</summary>
    public int Port { get; private set; }

    public string Url => $"http://127.0.0.1:{Port}/mcp";

    public bool IsRunning => _listener is not null;

    /// <exception cref="SocketException">The port is taken.</exception>
    public void Start()
    {
        if (_listener is not null)
            return;
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _loop = AcceptLoopAsync(listener, _stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener?.Stop();
        _listener = null;
        if (_loop is not null)
        {
            try
            {
                // Not back on the caller's thread: the editor stops this from its UI thread while blocking it.
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = ServeAsync(client, stop);
        }
    }

    // One connection, possibly several requests (keep-alive).
    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new HttpReader(stream);
                while (!stop.IsCancellationRequested)
                {
                    var request = await reader.ReadRequestAsync(stop);
                    if (request is null)
                        return;
                    var response = await RespondAsync(request).WaitAsync(stop).ConfigureAwait(false);
                    await WriteAsync(stream, response, stop);
                    if (!request.KeepAlive)
                        return;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
            }
        }
    }

    private async Task<HttpResponse> RespondAsync(HttpRequest request)
    {
        if (request.Headers.TryGetValue("origin", out var origin) && !IsLocalOrigin(origin))
            return HttpResponse.Text(403, "Forbidden", "Only local programs may use Griff's MCP server.");

        var path = request.Path.Split('?')[0].TrimEnd('/');
        if (path is "" && request.Method == "GET")
            return HttpResponse.Text(200, "OK", $"Griff MCP server. POST JSON-RPC to {Url}.");
        if (path != "/mcp")
            return HttpResponse.Text(404, "Not Found", "Not found. The MCP endpoint is /mcp.");

        switch (request.Method)
        {
            case "POST":
                var answer = await _server.HandleAsync(Encoding.UTF8.GetString(request.Body));
                if (answer is null)
                    return new HttpResponse(202, "Accepted", [], null);
                var headers = new List<(string, string)>();
                if (request.Body.Length < 4096 && Encoding.UTF8.GetString(request.Body).Contains("\"initialize\"", StringComparison.Ordinal))
                    headers.Add(("Mcp-Session-Id", Guid.NewGuid().ToString("N")));
                return new HttpResponse(200, "OK", headers, ("application/json", Encoding.UTF8.GetBytes(answer)));
            case "DELETE":
                return new HttpResponse(200, "OK", [], null);
            case "GET":
                // No server-to-client event stream: everything comes back as the answer to a POST.
                return new HttpResponse(405, "Method Not Allowed", [("Allow", "POST, DELETE")], null);
            default:
                return new HttpResponse(405, "Method Not Allowed", [("Allow", "POST, DELETE")], null);
        }
    }

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host == "localhost");

    private static async Task WriteAsync(Stream stream, HttpResponse response, CancellationToken stop)
    {
        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {response.Status} {response.Reason}\r\n");
        foreach (var (name, value) in response.Headers)
            head.Append($"{name}: {value}\r\n");
        var body = response.Body?.Bytes ?? [];
        if (response.Body is { } content)
            head.Append($"Content-Type: {content.Type}\r\n");
        head.Append($"Content-Length: {body.Length}\r\n");
        head.Append("Cache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), stop);
        if (body.Length > 0)
            await stream.WriteAsync(body, stop);
        await stream.FlushAsync(stop);
    }

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body, bool KeepAlive);

    private sealed record HttpResponse(int Status, string Reason, IReadOnlyList<(string Name, string Value)> Headers, (string Type, byte[] Bytes)? Body)
    {
        public static HttpResponse Text(int status, string reason, string text) =>
            new(status, reason, [], ("text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text)));
    }

    // Just enough HTTP/1.1 for JSON-RPC: a request line, headers, and a Content-Length or chunked body.
    private sealed class HttpReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[16 * 1024];
        private int _start;
        private int _end;

        public async Task<HttpRequest?> ReadRequestAsync(CancellationToken stop)
        {
            var requestLine = await ReadLineAsync(stop);
            if (requestLine is null)
                return null;
            if (requestLine.Length == 0)
                requestLine = await ReadLineAsync(stop) ?? throw new InvalidDataException();
            var parts = requestLine.Split(' ');
            if (parts.Length < 3)
                throw new InvalidDataException("Bad request line.");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await ReadLineAsync(stop) is { Length: > 0 } line)
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            byte[] body;
            if (headers.TryGetValue("transfer-encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                using var chunks = new MemoryStream();
                while (true)
                {
                    var sizeLine = await ReadLineAsync(stop) ?? throw new InvalidDataException();
                    var size = Convert.ToInt32(sizeLine.Split(';')[0].Trim(), 16);
                    if (size == 0)
                    {
                        while (await ReadLineAsync(stop) is { Length: > 0 })
                        {
                        }

                        break;
                    }

                    if (chunks.Length + size > MaxBodyBytes)
                        throw new InvalidDataException("Body too large.");
                    chunks.Write(await ReadBytesAsync(size, stop));
                    await ReadLineAsync(stop);
                }

                body = chunks.ToArray();
            }
            else
            {
                var length = headers.TryGetValue("content-length", out var text) && int.TryParse(text, out var n) ? n : 0;
                if (length > MaxBodyBytes)
                    throw new InvalidDataException("Body too large.");
                body = await ReadBytesAsync(length, stop);
            }

            var keepAlive = !(headers.TryGetValue("connection", out var connection) && connection.Equals("close", StringComparison.OrdinalIgnoreCase))
                && parts[2] != "HTTP/1.0";
            return new HttpRequest(parts[0].ToUpperInvariant(), parts[1], headers, body, keepAlive);
        }

        private async Task<string?> ReadLineAsync(CancellationToken stop)
        {
            var line = new List<byte>();
            while (true)
            {
                if (_start == _end && !await FillAsync(stop))
                    return line.Count == 0 ? null : throw new InvalidDataException("Connection closed mid-line.");
                var b = _buffer[_start++];
                if (b == '\n')
                    return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
                line.Add(b);
                if (line.Count > 64 * 1024)
                    throw new InvalidDataException("Header line too long.");
            }
        }

        private async Task<byte[]> ReadBytesAsync(int count, CancellationToken stop)
        {
            var result = new byte[count];
            var written = 0;
            while (written < count)
            {
                if (_start == _end && !await FillAsync(stop))
                    throw new InvalidDataException("Connection closed mid-body.");
                var take = Math.Min(count - written, _end - _start);
                Array.Copy(_buffer, _start, result, written, take);
                _start += take;
                written += take;
            }

            return result;
        }

        private async Task<bool> FillAsync(CancellationToken stop)
        {
            _start = 0;
            _end = await stream.ReadAsync(_buffer, stop);
            return _end > 0;
        }
    }
}
