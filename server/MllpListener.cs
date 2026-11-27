using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LabAlert;

public sealed class MllpListener(IConfiguration configuration, ResultStore store, ILogger<MllpListener> logger) : BackgroundService
{
    private const int MaxMessageBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var address = IPAddress.Parse(configuration["Mllp:Address"] ?? "127.0.0.1");
        var port = configuration.GetValue("Mllp:Port", 2575);
        var listener = new TcpListener(address, port);
        listener.Start();
        logger.LogInformation("MLLP listener started on {Address}:{Port}", address, port);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var buffer = new byte[4096];
                using var message = new MemoryStream();
                var state = 0;
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, ct);
                    if (count == 0) return;
                    for (var i = 0; i < count; i++)
                    {
                        var b = buffer[i];
                        if (state == 0)
                        {
                            if (b != 0x0b) throw new InvalidDataException("Invalid MLLP frame.");
                            message.SetLength(0);
                            state = 1;
                        }
                        else if (state == 1)
                        {
                            if (b == 0x1c) state = 2;
                            else
                            {
                                if (message.Length >= MaxMessageBytes) throw new InvalidDataException("MLLP frame too large.");
                                message.WriteByte(b);
                            }
                        }
                        else
                        {
                            if (b != 0x0d) throw new InvalidDataException("Invalid MLLP frame terminator.");
                            var text = Utf8.GetString(message.ToArray());
                            var ack = await ProcessAsync(text, ct);
                            var bytes = Utf8.GetBytes("\x0b" + ack + "\x1c\x0d");
                            await stream.WriteAsync(bytes, ct);
                            state = 0;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or DecoderFallbackException)
            {
                logger.LogWarning("MLLP connection closed due to invalid data or a connection error.");
            }
        }
    }

    private async Task<string> ProcessAsync(string text, CancellationToken ct)
    {
        var header = text.Split('\r')[0];
        if (header.Length < 4 || !header.StartsWith("MSH", StringComparison.Ordinal))
            throw new InvalidDataException("Missing MSH.");
        var separator = header[3];
        var fields = header.Split(separator);
        if (fields.Length < 12 || fields[1].Length != 4 || fields[9].Length == 0 ||
            fields[11].Length == 0 || separator < 0x20)
            throw new InvalidDataException("Invalid MSH.");
        var code = "AA";
        try
        {
            var results = Hl7Results.Parse(text);
            await store.SaveAsync(results, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("MLLP message could not be processed or saved.");
            code = "AE";
        }
        var enc = fields[1];
        var type = $"ACK{enc[0]}R01{enc[0]}ACK";
        return string.Join(separator, "MSH", enc, fields[4], fields[5], fields[2], fields[3],
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"), "", type, Guid.NewGuid().ToString("N"),
            fields[10], fields[11]) + "\r" + string.Join(separator, "MSA", code, fields[9]) + "\r";
    }
}
