using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace _3_ugers_gui_gruppe4;

public sealed class UrRobotClient
{
    private readonly string _host;
    private readonly int _port;

    public UrRobotClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>
    /// Sender et URScript program til robotten via TCP.
    /// Timeout er vigtig, ellers kan ConnectAsync hænge længe hvis IP/port er forkert eller robotten ikke lytter.
    /// </summary>
    public Task SendScriptAsync(string script, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(2);
        return SendScriptInternalAsync(script, timeout.Value, ct);
    }

    private async Task SendScriptInternalAsync(string script, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("Script is empty.", nameof(script));

        // Kombinér ct + timeout i én token
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        try
        {
            using var client = new TcpClient();

            // Connect (kan ellers hænge længe uden timeout)
            await client.ConnectAsync(_host, _port, token).ConfigureAwait(false);

            await using var stream = client.GetStream();

            // URScript sendes typisk som ASCII + newline
            var payload = Encoding.ASCII.GetBytes(script.TrimEnd() + "\n");

            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout connecting/sending to robot at {_host}:{_port} (timeout={timeout.TotalSeconds:0.##}s).");
        }
        catch (SocketException ex)
        {
            // Gør Socket-fejl mere læselig i loggen
            throw new InvalidOperationException($"Socket error to robot {_host}:{_port}: {ex.SocketErrorCode} ({ex.Message})", ex);
        }
    }
}
