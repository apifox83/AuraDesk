using System.Net;
using System.Net.NetworkInformation;

namespace AuraDeskService.Mesh;

/// <summary>
/// Représente un nœud du réseau mesh AuraDesk.
/// Identifié de façon absolue par son adresse MAC.
/// </summary>
public class MeshNode
{
    // ── Identité absolue ──────────────────────────────────────────────────
    public string       Mac        { get; set; } = "";   // aa:bb:cc:dd:ee:ff
    public string       Hostname   { get; set; } = "";
    public string       PosteId    { get; set; } = "";   // LAN-XXXX

    // ── Adressage ─────────────────────────────────────────────────────────
    public string       PhysicalIp { get; set; } = "";   // IP LAN courante (peut changer)
    public string       VirtualIp  { get; set; } = "";   // IP attribuée par le mesh (172.16.x.x)

    // ── État ──────────────────────────────────────────────────────────────
    public MeshNodeState State     { get; set; } = MeshNodeState.Unknown;
    public DateTimeOffset FirstSeen{ get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    // ── Score réseau (qualité de liaison) ─────────────────────────────────
    public double       Score      { get; set; } = 0;    // 0-100, plus haut = meilleur relais
    public double       AvgLatency { get; set; } = 0;    // ms
    public double       PacketLoss { get; set; } = 0;    // % perte paquets
    public int          Uptime     { get; set; } = 0;    // secondes en ligne

    // ── Voisins connus de ce nœud ─────────────────────────────────────────
    public List<string> Neighbors  { get; set; } = new(); // liste de MAC

    // ── Hash de table (sync distribuée) ───────────────────────────────────
    public string       TableHash  { get; set; } = "";

    // ── Calcul du score ───────────────────────────────────────────────────
    public void ComputeScore()
    {
        // Formule : latence faible + perte faible + uptime élevé = bon relais
        double latScore  = Math.Max(0, 100 - AvgLatency);       // 0ms=100pts, 100ms=0pts
        double lossScore = Math.Max(0, 100 - PacketLoss * 10);  // 0%=100pts, 10%=0pts
        double upScore   = Math.Min(100, Uptime / 36.0);        // 1h=100pts
        Score = (latScore * 0.5) + (lossScore * 0.35) + (upScore * 0.15);
    }

    public bool IsOnline  => State == MeshNodeState.Online  && DateTimeOffset.UtcNow - LastSeen < TimeSpan.FromSeconds(15);
    public bool IsSilent  => State == MeshNodeState.Silent;
    public bool IsReachable => !string.IsNullOrEmpty(PhysicalIp) || !string.IsNullOrEmpty(VirtualIp);
}

public enum MeshNodeState
{
    Unknown,    // jamais vu
    Online,     // heartbeat UDP normal actif
    Silent,     // répond aux probes MAC mais pas de heartbeat UDP
    Offline,    // ne répond plus du tout
    New,        // premier contact, pas encore d'IP attribuée
}
