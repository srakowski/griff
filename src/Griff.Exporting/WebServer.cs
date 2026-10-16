using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Griff.Exporting;

/// <summary>
/// Serves a folder (a web export) to this computer only, at <c>http://127.0.0.1:{port}/</c>, so a game exported for the
/// web can be tried in a browser before it's uploaded: a browser won't run .NET from files opened straight off the disk.
/// It answers GET and HEAD with the folder's files (a folder with its <c>index.html</c>), each with its web type, never
/// cached, and nothing outside the folder.
/// </summary>
public sealed class WebServer : IAsyncDisposable
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json",
        [".wasm"] = "application/wasm",
        [".css"] = "text/css; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".mp3"] = "audio/mpeg",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
    };

    private readonly string _root;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _loop;

    /// <param name="folder">The folder to serve.</param>
    /// <param name="port">The port to listen on; 0 for any that's free.</param>
    public WebServer(string folder, int port = 0)
    {
        _root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Port = port;
    }

    /// <summary>The folder served.</summary>
    public string Folder => _root.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>The port asked for, or once started, the one listened on.</summary>
    public int Port { get; private set; }

    /// <summary>Where a browser on this computer opens it.</summary>
    public string Url => $"http://127.0.0.1:{Port}/";

    public bool IsRunning => _listener is not null;

    /// <summary>Starts listening (on the loopback address only, so nothing else on the network can reach it).</summary>
    /// <exception cref="SocketException">The port is taken.</exception>
    public void Start()
    {
        if (_listener is not null)
            return;
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _loop = AcceptAsync(listener, _stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        _listener = null;
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
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

    // One connection: requests one after another while the browser keeps it open.
    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                while (!stop.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(stop).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line))
                        return;
                    var close = false;
                    string? header;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(stop).ConfigureAwait(false)))
                    {
                        if (header.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) && header.Contains("close", StringComparison.OrdinalIgnoreCase))
                            close = true;
                    }

                    var parts = line.Split(' ');
                    await RespondAsync(stream, parts[0], parts.Length > 1 ? parts[1] : "/", stop).ConfigureAwait(false);
                    if (close || parts.Length < 3 || parts[2] == "HTTP/1.0")
                        return;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private async Task RespondAsync(Stream stream, string method, string target, CancellationToken stop)
    {
        if (method is not ("GET" or "HEAD"))
        {
            await WriteAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Only GET and HEAD."u8.ToArray(), head: false, stop).ConfigureAwait(false);
            return;
        }

        if (Resolve(target) is not { } file)
        {
            await WriteAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not found."u8.ToArray(), method == "HEAD", stop).ConfigureAwait(false);
            return;
        }

        var type = Types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
        await WriteAsync(stream, 200, "OK", type, await File.ReadAllBytesAsync(file, stop).ConfigureAwait(false), method == "HEAD", stop).ConfigureAwait(false);
    }

    /// <summary>The file a request's path names inside the folder (its index.html for a folder), or null for none or one outside it.</summary>
    internal string? Resolve(string target)
    {
        var path = target.Split('?', '#')[0];
        string relative;
        try
        {
            relative = Uri.UnescapeDataString(path).TrimStart('/');
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (relative.Contains('\0', StringComparison.Ordinal))
            return null;
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!(full + Path.DirectorySeparatorChar).StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return null;
        if (Directory.Exists(full))
            full = Path.Combine(full, WebExporter.Page);
        return File.Exists(full) ? full : null;
    }

    private static async Task WriteAsync(Stream stream, int status, string reason, string type, byte[] body, bool head, CancellationToken stop)
    {
        var text = string.Create(CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), stop).ConfigureAwait(false);
        if (!head)
            await stream.WriteAsync(body, stop).ConfigureAwait(false);
        await stream.FlushAsync(stop).ConfigureAwait(false);
    }
}
