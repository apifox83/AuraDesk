using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuraDeskService.Mesh;

/// <summary>
/// Table de routage distribuée du mesh AuraDesk.
/// Chaque nœud maintient une copie complète de tous les autres.
/// Synchronisation par hash — merge automatique au contact.
/// </summary>
public class MeshTable
{
    private readonly ConcurrentDictionary<string, MeshNode> _nodes = new();
    private string _selfMac = "";

    // Callback pour notifier l'UI (WebSocket broadcast)
    public Action<string>? OnChanged;

    // ── CRUD ──────────────────────────────────────────────────────────────

    public MeshNode GetOrCreate(string mac)
    {
        return _nodes.GetOrAdd(mac, m => new MeshNode { Mac = m, State = MeshNodeState.Unknown });
    }

    public void Set(MeshNode node)
    {
        _nodes[node.Mac] = node;
        NotifyChanged();
    }

    public List<MeshNode> GetAll()     => _nodes.Values.OrderBy(n => n.Hostname).ToList();
    public List<MeshNode> GetOnline()  => _nodes.Values.Where(n => n.IsOnline).ToList();
    public List<MeshNode> GetSilent()  => _nodes.Values.Where(n => n.IsSilent).ToList();
    public MeshNode?      Get(string mac) => _nodes.TryGetValue(mac, out var n) ? n : null;

    // ── Self ──────────────────────────────────────────────────────────────

    public void UpdateSelf(string mac, string hostname, string posteId, string ip)
    {
        _selfMac = mac;
        var self = GetOrCreate(mac);
        self.Hostname   = hostname;
        self.PosteId    = posteId;
        self.PhysicalIp = ip;
        self.State      = MeshNodeState.Online;
        self.LastSeen   = DateTimeOffset.UtcNow;
        self.Uptime++;
        self.ComputeScore();
        Set(self);
    }

    public double GetSelfScore() => Get(_selfMac)?.Score ?? 0;

    // ── Score ─────────────────────────────────────────────────────────────

    public void UpdateScore(string mac, double score, string physicalIp)
    {
        var node = GetOrCreate(mac);
        node.Score      = score;
        node.PhysicalIp = physicalIp;
        node.LastSeen   = DateTimeOffset.UtcNow;
        Set(node);
    }

    /// <summary>
    /// Met à jour la latence mesurée et recalcule le score.
    /// </summary>
    public void UpdateLatency(string mac, double latencyMs, bool success)
    {
        var node = GetOrCreate(mac);
        // Moyenne mobile exponentielle (alpha=0.3)
        node.AvgLatency  = node.AvgLatency == 0
            ? latencyMs
            : node.AvgLatency * 0.7 + latencyMs * 0.3;
        node.PacketLoss  = success
            ? node.PacketLoss * 0.9          // succès → perte diminue
            : node.PacketLoss * 0.9 + 10;   // échec  → perte augmente
        node.PacketLoss  = Math.Clamp(node.PacketLoss, 0, 100);
        node.ComputeScore();
        Set(node);
    }

    // ── Routage — meilleur relais ─────────────────────────────────────────

    /// <summary>
    /// Retourne le meilleur nœud relais pour atteindre une cible.
    /// Critère : score le plus élevé parmi les nœuds en ligne
    /// qui connaissent la cible comme voisin.
    /// </summary>
    public MeshNode? GetBestRelay(string targetMac)
    {
        // Si la cible est directement joignable
        var target = Get(targetMac);
        if (target != null && target.IsOnline) return target;

        // Chercher le meilleur relais qui connaît la cible
        return _nodes.Values
            .Where(n => n.IsOnline
                     && n.Mac != _selfMac
                     && n.Neighbors.Contains(targetMac))
            .OrderByDescending(n => n.Score)
            .FirstOrDefault();
    }

    /// <summary>
    /// Retourne le chemin complet vers une cible (liste de MAC).
    /// Simple pour l'instant — 1 saut max.
    /// </summary>
    public List<string> GetPath(string targetMac)
    {
        var direct = Get(targetMac);
        if (direct != null && direct.IsOnline)
            return new List<string> { targetMac };

        var relay = GetBestRelay(targetMac);
        if (relay != null)
            return new List<string> { relay.Mac, targetMac };

        return new List<string>(); // injoignable
    }

    // ── IP virtuelle ──────────────────────────────────────────────────────

    /// <summary>
    /// Alloue la prochaine IP virtuelle libre dans 172.16.0.x
    /// pour un poste silencieux/nouveau.
    /// </summary>
    public string AllocateVirtualIp()
    {
        var used = _nodes.Values
            .Where(n => !string.IsNullOrEmpty(n.VirtualIp))
            .Select(n => n.VirtualIp)
            .ToHashSet();

        for (int i = 1; i <= 253; i++)
        {
            var ip = $"172.16.0.{i}";
            if (!used.Contains(ip)) return ip;
        }
        return ""; // zone d'accueil pleine (254 postes simultanés)
    }

    // ── Synchronisation par hash ──────────────────────────────────────────

    /// <summary>
    /// Calcule un hash SHA256 de la table courante.
    /// Deux nœuds avec le même hash ont la même table.
    /// </summary>
    public string ComputeHash()
    {
        var ordered = _nodes.Values
            .OrderBy(n => n.Mac)
            .Select(n => $"{n.Mac}|{n.State}|{n.LastSeen.ToUnixTimeSeconds()}|{n.VirtualIp}")
            .ToList();
        var raw   = string.Join(";", ordered);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16]; // 16 chars suffisent
    }

    /// <summary>
    /// Merge une table reçue d'un autre nœud.
    /// Règle : l'entrée la plus récente (LastSeen) l'emporte.
    /// </summary>
    public void Merge(IEnumerable<MeshNodeSnapshot> remote)
    {
        bool changed = false;
        foreach (var snap in remote)
        {
            if (snap.Mac == _selfMac) continue; // ne jamais écraser soi-même

            var local = GetOrCreate(snap.Mac);
            if (snap.LastSeen > local.LastSeen.ToUnixTimeSeconds())
            {
                local.Hostname   = snap.Hostname;
                local.PosteId    = snap.PosteId;
                local.PhysicalIp = snap.PhysicalIp;
                local.VirtualIp  = snap.VirtualIp;
                local.Score      = snap.Score;
                local.State      = snap.State;
                local.LastSeen   = DateTimeOffset.FromUnixTimeSeconds(snap.LastSeen);
                _nodes[local.Mac] = local;
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    // ── Nettoyage ─────────────────────────────────────────────────────────

    /// <summary>
    /// Marque les nœuds absents depuis > 15s comme Offline.
    /// Libère les IP virtuelles après 24h d'absence.
    /// </summary>
    public void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        bool changed = false;
        foreach (var node in _nodes.Values)
        {
            if (node.Mac == _selfMac) continue;
            var elapsed = now - node.LastSeen;

            if (elapsed > TimeSpan.FromSeconds(15) && node.State == MeshNodeState.Online)
            { node.State = MeshNodeState.Offline; changed = true; }

            if (elapsed > TimeSpan.FromHours(24) && !string.IsNullOrEmpty(node.VirtualIp))
            { node.VirtualIp = ""; changed = true; } // libérer l'IP
        }
        if (changed) NotifyChanged();
    }

    // ── Notification UI ───────────────────────────────────────────────────

    private void NotifyChanged()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type  = "mesh_update",
            nodes = GetAll().Select(n => new {
                n.Mac, n.Hostname, n.PosteId, n.PhysicalIp,
                n.VirtualIp, n.Score, state = n.State.ToString(),
                n.IsOnline, n.IsSilent,
                lastSeen = n.LastSeen.ToUnixTimeSeconds()
            })
        });
        OnChanged?.Invoke(payload);
    }
}

// ── Snapshot pour la sérialisation sync ──────────────────────────────────

public class MeshNodeSnapshot
{
    public string         Mac        { get; set; } = "";
    public string         Hostname   { get; set; } = "";
    public string         PosteId    { get; set; } = "";
    public string         PhysicalIp { get; set; } = "";
    public string         VirtualIp  { get; set; } = "";
    public double         Score      { get; set; }
    public MeshNodeState  State      { get; set; }
    public long           LastSeen   { get; set; }
}
