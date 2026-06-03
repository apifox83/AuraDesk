using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace AuraDeskService.Mesh;

/// <summary>
/// Sonde le réseau pour détecter les postes silencieux.
/// Émet périodiquement "y a-t-il un poste silencieux ici ?"
/// Les postes sans heartbeat UDP répondent uniquement à cette probe.
/// </summary>
public class MeshProbe
{
    private readonly ILogger              _logger;
    private readonly RawSocketListener    _raw;
    private readonly MeshTable            _table;

    // MAC locale — identité absolue de ce poste
    private readonly string               _localMac;
    private readonly string               _localHostname;
    private readonly string               _localPosteId;

    // Intervalle de probe (plus long que heartbeat — pas de saturation)
    private const int PROBE_INTERVAL_MS   = 10000;  // 10s
    private const int REPLY_WINDOW_MS     = 2000;   // fenêtre de réponse 2s

    // Callback quand un nouveau poste silencieux est découvert
    public event Action<MeshNode>? OnSilentNodeFound;

    public MeshProbe(ILogger logger, RawSocketListener raw, MeshTable table)
    {
        _logger        = logger;
        _raw           = raw;
        _table         = table;
        _localMac      = GetLocalMac();
        _localHostname = Environment.MachineName;
        _localPosteId  = GeneratePosteId();

        // Écouter les réponses aux probes
        _raw.OnFrame += HandleFrame;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("MeshProbe démarré — MAC locale : {Mac}", _localMac);

        // S'enregistrer soi-même dans la table
        _table.UpdateSelf(_localMac, _localHostname, _localPosteId, GetLocalIp());

        while (!ct.IsCancellationRequested)
        {
            await ProbeAsync(ct);
            await Task.Delay(PROBE_INTERVAL_MS, ct);
        }
    }

    private async Task ProbeAsync(CancellationToken ct)
    {
        // Broadcast : "y a-t-il un poste silencieux ici ?"
        var probe = new MeshFrame
        {
            Type       = "probe",
            Mac        = _localMac,
            Hostname   = _localHostname,
            PosteId    = _localPosteId,
            PhysicalIp = GetLocalIp(),
            TableHash  = _table.ComputeHash(),
            Score      = _table.GetSelfScore(),
        };

        await _raw.BroadcastAsync(probe, ct);
        _logger.LogDebug("MeshProbe broadcast probe");

        // Attendre la fenêtre de réponse
        await Task.Delay(REPLY_WINDOW_MS, ct);
    }

    private void HandleFrame(MeshFrame frame)
    {
        switch (frame.Type)
        {
            case "probe":
                // Un autre poste probe — on répond pour lui signaler notre présence
                // (seulement si on a un heartbeat UDP actif = on est "en ligne")
                HandleIncomingProbe(frame);
                break;

            case "probe_reply":
                // Réponse d'un poste silencieux à notre probe
                HandleProbeReply(frame);
                break;

            case "probe_ack":
                // Accusé de réception — le poste sait qu'on l'a entendu
                break;

            case "score":
                // Mise à jour du score d'un nœud
                _table.UpdateScore(frame.Mac, frame.Score, frame.PhysicalIp);
                break;

            case "sync_request":
                // Un nœud demande notre table (hash différent du sien)
                _ = HandleSyncRequestAsync(frame);
                break;
        }
    }

    private void HandleIncomingProbe(MeshFrame frame)
    {
        if (frame.Mac == _localMac) return; // ignorer nos propres probes

        // Mettre à jour la table avec ce nœud
        var node = _table.GetOrCreate(frame.Mac);
        node.Hostname   = frame.Hostname;
        node.PosteId    = frame.PosteId;
        node.PhysicalIp = frame.SourceIp;
        node.LastSeen   = DateTimeOffset.UtcNow;
        node.State      = MeshNodeState.Online;
        _table.Set(node);

        // Si son hash de table diffère du nôtre → demander sync
        if (!string.IsNullOrEmpty(frame.TableHash) && frame.TableHash != _table.ComputeHash())
            _ = RequestSyncAsync(frame.SourceIp);
    }

    private void HandleProbeReply(MeshFrame frame)
    {
        if (frame.Mac == _localMac) return;

        _logger.LogInformation("Poste silencieux détecté : {Hostname} ({Mac})", frame.Hostname, frame.Mac);

        var node = _table.GetOrCreate(frame.Mac);
        node.Hostname   = frame.Hostname;
        node.PosteId    = frame.PosteId;
        node.PhysicalIp = frame.SourceIp;
        node.LastSeen   = DateTimeOffset.UtcNow;
        node.State      = MeshNodeState.Silent;

        // Attribuer une IP virtuelle si pas encore fait
        if (string.IsNullOrEmpty(node.VirtualIp))
            node.VirtualIp = _table.AllocateVirtualIp();

        _table.Set(node);

        // Notifier l'UI
        OnSilentNodeFound?.Invoke(node);

        // Accuser réception avec l'IP attribuée
        _ = AckSilentNodeAsync(frame.SourceIp, node.VirtualIp);
    }

    private async Task AckSilentNodeAsync(string targetIp, string virtualIp)
    {
        var ack = new MeshFrame
        {
            Type       = "probe_ack",
            Mac        = _localMac,
            Hostname   = _localHostname,
            PosteId    = _localPosteId,
            PhysicalIp = GetLocalIp(),
            VirtualIp  = virtualIp,
        };
        await _raw.SendAsync(targetIp, ack);
    }

    private async Task RequestSyncAsync(string targetIp)
    {
        var req = new MeshFrame
        {
            Type       = "sync_request",
            Mac        = _localMac,
            PhysicalIp = GetLocalIp(),
            TableHash  = _table.ComputeHash(),
        };
        await _raw.SendAsync(targetIp, req);
    }

    private async Task HandleSyncRequestAsync(MeshFrame frame)
    {
        // Envoyer notre table complète
        var nodes   = _table.GetAll();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type  = "sync_data",
            mac   = _localMac,
            nodes = nodes.Select(n => new {
                n.Mac, n.Hostname, n.PosteId, n.PhysicalIp,
                n.VirtualIp, n.Score, n.State,
                LastSeen = n.LastSeen.ToUnixTimeSeconds()
            })
        });
        // On envoie via WebSocket si le nœud est joignable
        // (le RawSocket transporte uniquement les frames légères)
        _logger.LogDebug("Sync envoyée à {Ip}", frame.SourceIp);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string GetLocalMac()
    {
        var iface = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();
        if (iface == null) return "00:00:00:00:00:00";
        var bytes = iface.GetPhysicalAddress().GetAddressBytes();
        return string.Join(":", bytes.Select(b => b.ToString("x2")));
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

    private static string GeneratePosteId()
    {
        var hex = BitConverter.ToString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(Environment.MachineName)
            )
        ).Replace("-", "").Substring(0, 8).ToUpper();
        return $"LAN-{hex[..4]}";
    }
}
