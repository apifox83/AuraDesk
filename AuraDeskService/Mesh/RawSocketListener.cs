using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AuraDeskService.Mesh;

/// <summary>
/// Écoute les trames brutes sur l'interface réseau locale.
/// Utilise SOCK_RAW + SIO_RCVALL — nécessite droits administrateur.
/// Filtre les trames AuraDesk via un magic header custom (0xAU2A).
/// </summary>
public class RawSocketListener : IDisposable
{
    private readonly ILogger        _logger;
    private Socket?                 _socket;
    private bool                    _disposed;

    // Magic bytes AuraDesk dans le payload UDP (évite de parser tout le trafic)
    public static readonly byte[]   AURA_MAGIC = { 0x41, 0x55, 0x52, 0x41 }; // "AURA"
    public static readonly int      AURA_PORT  = 47202; // port dédié mesh (≠ heartbeat 47200)

    // Callback quand une trame AuraMesh est reçue
    public event Action<MeshFrame>? OnFrame;

    public RawSocketListener(ILogger logger) { _logger = logger; }

    public async Task StartAsync(CancellationToken ct)
    {
        var localIp = GetLocalIp();
        if (localIp == null) { _logger.LogWarning("RawSocket: aucune IP locale trouvée"); return; }

        try
        {
            // Socket RAW sur UDP — on écoute tout le trafic entrant
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(localIp, 0));
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);

            // SIO_RCVALL : recevoir TOUS les paquets de l'interface (mode promiscuous)
            _socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), BitConverter.GetBytes(1));

            _logger.LogInformation("RawSocket en écoute sur {Ip}", localIp);

            var buffer = new byte[65535];
            while (!ct.IsCancellationRequested && !_disposed)
            {
                try
                {
                    var received = await _socket.ReceiveAsync(buffer, SocketFlags.None, ct);
                    if (received > 28) // IP header (20) + UDP header (8) minimum
                        ParsePacket(buffer, received);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (!_disposed)
                { _logger.LogWarning("RawSocket recv: {Msg}", ex.Message); }
            }
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("RawSocket: {Msg} (droits admin requis)", ex.Message);
        }
    }

    private void ParsePacket(byte[] buf, int len)
    {
        // ── Lire l'en-tête IP ────────────────────────────────────────────
        int ipHeaderLen = (buf[0] & 0x0F) * 4;
        if (len < ipHeaderLen + 8) return;

        byte protocol = buf[9];
        if (protocol != 17) return; // 17 = UDP uniquement

        // ── Lire l'en-tête UDP ───────────────────────────────────────────
        int udpOffset  = ipHeaderLen;
        int srcPort    = (buf[udpOffset]     << 8) | buf[udpOffset + 1];
        int dstPort    = (buf[udpOffset + 2] << 8) | buf[udpOffset + 3];
        int udpLen     = (buf[udpOffset + 4] << 8) | buf[udpOffset + 5];

        if (dstPort != AURA_PORT && srcPort != AURA_PORT) return;

        int payloadOffset = udpOffset + 8;
        int payloadLen    = udpLen - 8;
        if (payloadLen < 5 || payloadOffset + payloadLen > len) return;

        // ── Vérifier le magic AURA ───────────────────────────────────────
        for (int i = 0; i < 4; i++)
            if (buf[payloadOffset + i] != AURA_MAGIC[i]) return;

        // ── Extraire l'IP source ─────────────────────────────────────────
        string srcIp = $"{buf[12]}.{buf[13]}.{buf[14]}.{buf[15]}";

        // ── Parser le JSON (après les 4 bytes magic) ──────────────────────
        try
        {
            var json  = Encoding.UTF8.GetString(buf, payloadOffset + 4, payloadLen - 4);
            var frame = JsonSerializer.Deserialize<MeshFrame>(json);
            if (frame != null)
            {
                frame.SourceIp = srcIp;
                OnFrame?.Invoke(frame);
            }
        }
        catch { /* trame malformée, ignorée */ }
    }

    // ── Envoi d'une trame AuraMesh ────────────────────────────────────────

    public async Task SendAsync(string targetIp, MeshFrame frame, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        var json    = JsonSerializer.Serialize(frame);
        var payload = new byte[4 + Encoding.UTF8.GetByteCount(json)];
        AURA_MAGIC.CopyTo(payload, 0);
        Encoding.UTF8.GetBytes(json, 0, json.Length, payload, 4);
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse(targetIp), AURA_PORT));
    }

    public async Task BroadcastAsync(MeshFrame frame, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        var json    = JsonSerializer.Serialize(frame);
        var payload = new byte[4 + Encoding.UTF8.GetByteCount(json)];
        AURA_MAGIC.CopyTo(payload, 0);
        Encoding.UTF8.GetBytes(json, 0, json.Length, payload, 4);
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, AURA_PORT));
    }

    private static IPAddress? GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _disposed = true;
        _socket?.Close();
        _socket?.Dispose();
    }
}

// ── Modèle de trame AuraMesh ──────────────────────────────────────────────

public class MeshFrame
{
    public string Type      { get; set; } = "";  // probe / probe_reply / score / sync / relay
    public string Mac       { get; set; } = "";  // MAC de l'émetteur
    public string Hostname  { get; set; } = "";
    public string PosteId   { get; set; } = "";
    public string PhysicalIp{ get; set; } = "";
    public string VirtualIp { get; set; } = "";  // IP attribuée si connue
    public string TableHash { get; set; } = "";  // hash table de routage
    public double Score     { get; set; } = 0;
    public long   Ts        { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Champs non sérialisés (remplis à la réception)
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceIp  { get; set; } = "";
}
