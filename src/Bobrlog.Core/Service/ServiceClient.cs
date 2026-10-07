using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Service;

/// <summary>Low-level client: one connection per request.</summary>
public sealed class ServiceClient(string? socketPath = null)
{
    public string SocketPath { get; } = socketPath ?? ServiceProtocol.SocketPath;

    public bool SocketExists => File.Exists(SocketPath);

    public async IAsyncEnumerable<string> RequestAsync(ServiceRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct).ConfigureAwait(false);

        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var payload = Encoding.UTF8.GetBytes(ServiceProtocol.Serialize(request) + "\n");
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (IOException) when (ct.IsCancellationRequested)
            {
                yield break;
            }
            if (line is null)
                yield break;
            if (ServiceProtocol.TryGetError(line) is { } error)
                throw new Sources.JournalSourceException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Source_ServiceError, error));
            yield return line;
        }
    }

    public async Task<ServiceHello?> PingAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!SocketExists)
            return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await foreach (var line in RequestAsync(new ServiceRequest(ServiceOperation.Ping), cts.Token).ConfigureAwait(false))
                return ServiceProtocol.DeserializeHello(line);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException
                                      or Sources.JournalSourceException or System.Text.Json.JsonException)
        {
        }
        return null;
    }
}
