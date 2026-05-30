using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AuraDeskService.Network;

public class UdpHeartbeat
{
    private readonly ILogger _logger;
    private const int Port = 47200;
    private const int IntervalMs = 3000;

    private static readonly string _posteName = Environment.MachineName;
    private static readonly string _posteId = GenerateId();

    public UdpHeartbeat(ILogger logger)
    {
        _logger = logger;
    }

    private static string GenerateId()
    {
        var hex = BitConverter.ToString(
            System.Security.Cryptography.MD5.HashData(
                Encoding.UTF8.GetBytes(Environment.MachineName)
            )
        ).Replace("-", "").Substring(0, 8).ToUpper();
        return $"LAN-{hex[..4]}";
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var sender = new UdpClient();
        sender.EnableBroadcast = true;
        using var receiver = new UdpClient(Port);
        receiver.EnableBroadcast = true;
        _logger.LogInformation("Heartbeat UDP sur port {Port}", Port);
        await Task.WhenAll(SendLoopAsync(sender, ct), ReceiveLoopAsync(receiver, ct));
    }

    private async Task SendLoopAsync(UdpClient sender, CancellationToken ct)
    {
        var ep = new IPEndPoint(IPAddress.Broadcast, Port);
        while (!ct.IsCancellationRequested)
        {
            var payload = new { type = "heartbeat", id = _posteId, name = _posteName, ip = GetLocalIp(), os = Environment.OSVersion.Platform.ToString(), ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            var data = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await sender.SendAsync(data, data.Length, ep);
            await Task.Delay(IntervalMs, ct);
        }
    }

    private async Task ReceiveLoopAsync(UdpClient receiver, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await receiver.ReceiveAsync(ct);
                PosteRegistry.Update(Encoding.UTF8.GetString(result.Buffer));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning("Heartbeat recv: {Msg}", ex.Message); }
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "0.0.0.0";
        }
        catch { return "0.0.0.0"; }
    }
}
