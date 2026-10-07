using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Bobrlog.Core.Analysis;
using Bobrlog.Core.Platform;
using Bobrlog.Core.Resources;
using Bobrlog.Core.Service;
using Bobrlog.Core.Sources;

namespace Bobrlog.Service;

/// <summary>
/// Serves journal queries over a Unix domain socket. Only root, the service's own user and the UIDs listed in
/// /etc/bobrlog/allowed-uids (verified with SO_PEERCRED) may connect.
/// </summary>
public sealed class SocketServer(ILogger<SocketServer> logger) : BackgroundService
{
    private const int MaxConcurrentClients = 16;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentClients);
    private readonly string _journalctl = ProcessLines.FindExecutable("journalctl") ?? "journalctl";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var socketPath = ServiceProtocol.SocketPath;
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        if (File.Exists(socketPath))
            File.Delete(socketPath);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        // Anyone may connect; authorization happens per connection via peer credentials.
        File.SetUnixFileMode(socketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        listener.Listen(32);
        logger.LogInformation("Listening on {Path}", socketPath);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            File.Delete(socketPath);
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken stoppingToken)
    {
        using var connection = socket;
        uint uid;
        try
        {
            (_, uid, _) = PeerCredentials.Get(socket);
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Could not read peer credentials");
            return;
        }

        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };

        if (uid != 0 && uid != Native.CurrentUid && !ServiceInstaller.ReadAllowedUids(ServiceProtocol.AllowedUidsPath).Contains(uid))
        {
            logger.LogWarning("Rejected connection from uid {Uid}", uid);
            await TryWriteAsync(writer, ServiceProtocol.Error(string.Format(CultureInfo.InvariantCulture, CoreStrings.Service_NotAuthorized, uid)));
            return;
        }

        if (!await _slots.WaitAsync(TimeSpan.FromSeconds(10), stoppingToken))
        {
            await TryWriteAsync(writer, ServiceProtocol.Error(CoreStrings.Service_TooManyRequests));
            return;
        }

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var line = await reader.ReadLineAsync(timeout.Token);
            var request = line is null ? null : ServiceProtocol.DeserializeRequest(line);
            if (request is null)
            {
                await TryWriteAsync(writer, ServiceProtocol.Error(CoreStrings.Service_InvalidRequest));
                return;
            }

            // The client closing the connection cancels the running journalctl.
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _ = WatchDisconnectAsync(reader, requestCts);

            await ProcessAsync(request, writer, requestCts.Token);
            await writer.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            // client went away
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Request failed");
            await TryWriteAsync(writer, ServiceProtocol.Error(ex.Message));
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task ProcessAsync(ServiceRequest request, StreamWriter writer, CancellationToken ct)
    {
        switch (request.Op)
        {
            case ServiceOperation.Ping:
                await writer.WriteLineAsync(ServiceProtocol.Serialize(new ServiceHello(ServiceProtocol.Version, Native.CurrentUid,
                    typeof(SocketServer).Assembly.GetName().Version?.ToString() ?? "?")));
                break;

            case ServiceOperation.Query:
            {
                var query = Sanitize(request.Query ?? new());
                // Raw journalctl lines are forwarded; the client parses and classifies them.
                var lines = ProcessLines.RunAsync(_journalctl, JournalctlArguments.Build(query), ct);
                await foreach (var line in JournalctlArguments.ApplyLimit(lines, query).WithCancellation(ct))
                    await writer.WriteLineAsync(line.AsMemory(), ct);
                break;
            }

            case ServiceOperation.Boots:
            {
                var (_, stdout, stderr) = await ProcessLines.RunToEndAsync(_journalctl, ["--list-boots", "-o", "json", "--no-pager"], ct);
                await writer.WriteLineAsync(string.IsNullOrWhiteSpace(stdout)
                    ? ServiceProtocol.Error(stderr.Trim())
                    : stdout.ReplaceLineEndings(" ").Trim());
                break;
            }

            case ServiceOperation.Crashes:
                var crashes = await CrashReportReader.ReadAllAsync(ct);
                await writer.WriteLineAsync(ServiceProtocol.Serialize(crashes.ToList()));
                break;

            default:
                await writer.WriteLineAsync(ServiceProtocol.Error(CoreStrings.Service_UnknownOperation));
                break;
        }
    }

    private static Core.Models.JournalQuery Sanitize(Core.Models.JournalQuery query) => query with
    {
        Limit = query.Limit is null ? ServiceProtocol.MaxLimit : Math.Clamp(query.Limit.Value, 1, ServiceProtocol.MaxLimit),
    };

    private static async Task WatchDisconnectAsync(StreamReader reader, CancellationTokenSource cts)
    {
        try
        {
            // Clients never send more than the request line; EOF means they disconnected.
            while (await reader.ReadLineAsync(cts.Token) is not null)
            {
            }
        }
        catch (Exception)
        {
        }
        try
        {
            await cts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task TryWriteAsync(StreamWriter writer, string line)
    {
        try
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
