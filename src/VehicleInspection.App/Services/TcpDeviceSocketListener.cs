using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VehicleInspection.App.Services;

public sealed class TcpDeviceSocketListener : IAsyncDisposable
{
    private readonly FrontendDeviceIngestionForwarder _ingestionService;
    private readonly IPAddress _bindAddress;
    private readonly int _port;
    private readonly int _maxMessageBytes;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public TcpDeviceSocketListener(FrontendDeviceIngestionForwarder ingestionService, IPAddress bindAddress, int port, int maxMessageBytes)
    {
        _ingestionService = ingestionService;
        _bindAddress = bindAddress;
        _port = port;
        _maxMessageBytes = maxMessageBytes;
    }

    public event EventHandler<string>? StatusChanged;

    public Task StartAsync()
    {
        if (_listener != null)
        {
            return Task.CompletedTask;
        }

        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(_bindAddress, _port);
        _listener.Start();
        StatusChanged?.Invoke(this, $"Socket listener active on {_bindAddress}:{_port}");
        _ = AcceptLoopAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        _listener = null;
        StatusChanged?.Invoke(this, "Socket listener stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cancellation?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Socket accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        client.ReceiveTimeout = 10_000;

        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await ReadBoundedLineAsync(reader, cancellationToken);
                if (line == null)
                {
                    break;
                }

                try
                {
                    var record = await _ingestionService.ProcessJsonAsync(line, cancellationToken);
                    StatusChanged?.Invoke(this, record == null ? "Duplicate device payload ignored" : $"Ingested trigger {record.TriggerId}");
                }
                catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or FormatException)
                {
                    StatusChanged?.Invoke(this, $"Rejected device payload: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            StatusChanged?.Invoke(this, $"Socket client error: {ex.Message}");
        }
    }

    private async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    return builder.ToString().TrimEnd('\r');
                }

                builder.Append(buffer[i]);
                if (builder.Length > _maxMessageBytes)
                {
                    throw new InvalidDataException("Socket message exceeds the configured size limit.");
                }
            }
        }

        return null;
    }
}
