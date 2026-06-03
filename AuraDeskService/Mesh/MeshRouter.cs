using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AuraDeskService.Mesh;

/// <summary>
/// Moteur de routage mesh AuraDesk.
/// - Mesure la qualité des liaisons entre nœuds (latence, perte)
/// - Calcule le meilleur chemin vers chaque nœud
/// - Bascule automatiquement si le relais actif tombe
/// - Expose les stats à l'UI via WebSocket
/// </summary>
public class MeshRouter
{
    private readonly ILogger           _logger;
    private readonly MeshTable         _table;
    private readonly RawSocketListener _raw;
    private readonly string            _localMac;

    // Chemins actifs : MAC cible → MAC relais actuel
    private readonly ConcurrentDictionary<string, string> _activePaths = new();

    // Intervalle de mesure de qualité
    private const int MEASURE_INTERVAL_MS = 5000;   // 5s
    private const int PING_TIMEOUT_MS     = 1000;   // 1s
    private const int PING_COUNT          = 3;      // pings par mesure

    // Callback pour notifier l'UI d'une bascule
    public Action<string, string, string>? OnPathSwitch; // targetMac, oldRelay, newRelay

    public MeshRouter(ILogger logger, MeshTable table, RawSocketListener raw)
    {
        _logger   = logger;
        _table    = table;
        _raw      = raw;
        _localMac = GetLocalMac();
        _raw.OnFrame += HandleFrame;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("MeshRouter démarré");

        // Boucle de mesure de qualité en parallèle
        _ = Task.Run(() => CleanupLoopAsync(ct), ct);

        while (!ct.IsCancellationRequested)
        {
            await MeasureAllAsync(ct);
            await Task.Delay(MEASURE_INTERVAL_MS, ct);
        }
    }

    // ── Mesure de qualité ─────────────────────────────────────────────────

    private async Task MeasureAllAsync(CancellationToken ct)
    {
        var online = _table.GetOnline()
                           .Where(n => n.Mac != _localMac)
                           .ToList();

        // Mesurer tous les nœuds en parallèle
        await Task.WhenAll(online.Select(n => MeasureNodeAsync(n, ct)));
    }

    private async Task MeasureNodeAsync(MeshNode node, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(node.PhysicalIp) || node.PhysicalIp == "0.0.0.0") return;

        double totalMs  = 0;
        int    success  = 0;

        for (int i = 0; i < PING_COUNT; i++)
        {
            var (ok, ms) = await PingAsync(node.PhysicalIp, ct);
            if (ok) { totalMs += ms; success++; }
            await Task.Delay(200, ct); // espacer les pings
        }

        double avgMs = success > 0 ? totalMs / success : 999;
        _table.UpdateLatency(node.Mac, avgMs, success > 0);

        // Diffuser notre score mis à jour
        await BroadcastScoreAsync(ct);

        // Vérifier si le chemin actif doit basculer
        CheckPathSwitch(node.Mac);
    }

    private static async Task<(bool ok, double ms)> PingAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var sw     = Stopwatch.StartNew();
            var reply  = await ping.SendPingAsync(ip, PING_TIMEOUT_MS);
            sw.Stop();
            return (reply.Status == IPStatus.Success, sw.Elapsed.TotalMilliseconds);
        }
        catch { return (false, 999); }
    }

    // ── Gestion des chemins ───────────────────────────────────────────────

    /// <summary>
    /// Retourne le meilleur relais pour atteindre une cible.
    /// Bascule automatiquement si le relais actuel est dégradé.
    /// </summary>
    public MeshNode? GetRelay(string targetMac)
    {
        var best = _table.GetBestRelay(targetMac);
        if (best == null) return null;

        // Enregistrer le chemin actif
        if (_activePaths.TryGetValue(targetMac, out var current) && current != best.Mac)
        {
            _logger.LogInformation("Bascule chemin vers {Target} : {Old} → {New}",
                targetMac, current, best.Mac);
            OnPathSwitch?.Invoke(targetMac, current, best.Mac);
        }
        _activePaths[targetMac] = best.Mac;
        return best;
    }

    private void CheckPathSwitch(string targetMac)
    {
        if (!_activePaths.ContainsKey(targetMac)) return;
        var best = _table.GetBestRelay(targetMac);
        if (best == null) return;

        var current = _activePaths[targetMac];
        if (current == best.Mac) return;

        // Bascule si le nouveau score est significativement meilleur (+10pts)
        var currentNode = _table.Get(current);
        if (currentNode == null || best.Score > currentNode.Score + 10)
        {
            _logger.LogInformation("Bascule automatique vers {Target} via {New} (score {Score:F0})",
                targetMac, best.Mac, best.Score);
            _activePaths[targetMac] = best.Mac;
            OnPathSwitch?.Invoke(targetMac, current, best.Mac);
        }
    }

    // ── Réception des frames score ────────────────────────────────────────

    private void HandleFrame(MeshFrame frame)
    {
        if (frame.Type != "score") return;
        _table.UpdateScore(frame.Mac, frame.Score, frame.PhysicalIp);
    }

    // ── Diffusion du score local ──────────────────────────────────────────

    private async Task BroadcastScoreAsync(CancellationToken ct)
    {
        var self = _table.Get(_localMac);
        if (self == null) return;

        var frame = new MeshFrame
        {
            Type       = "score",
            Mac        = _localMac,
            Score      = self.Score,
            PhysicalIp = self.PhysicalIp,
            TableHash  = _table.ComputeHash(),
        };
        await _raw.BroadcastAsync(frame, ct);
    }

    // ── Nettoyage périodique ──────────────────────────────────────────────

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _table.Cleanup();
            await Task.Delay(10000, ct); // toutes les 10s
        }
    }

    // ── Résumé pour l'UI ──────────────────────────────────────────────────

    public object GetStatus()
    {
        return new
        {
            nodes = _table.GetAll().Select(n => new
            {
                n.Mac, n.Hostname, n.PhysicalIp, n.VirtualIp,
                n.Score, n.AvgLatency, n.PacketLoss,
                state    = n.State.ToString(),
                isOnline = n.IsOnline,
                isSilent = n.IsSilent,
                path     = _activePaths.TryGetValue(n.Mac, out var relay) ? relay : "direct"
            }),
            paths = _activePaths.Select(kv => new { target = kv.Key, relay = kv.Value })
        };
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
}
