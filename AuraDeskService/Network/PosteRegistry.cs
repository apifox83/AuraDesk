using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace AuraDeskService.Network;

public static class PosteRegistry
{
    private static readonly ConcurrentDictionary<string, PosteEntry> _postes = new();
    private const int TimeoutSeconds = 15;

    private static readonly HashSet<string> _localIps = new(
        Dns.GetHostAddresses(Dns.GetHostName())
            .Select(a => a.ToString())
            .Append("127.0.0.1")
            .Append("::1")
    );

    public static void Update(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "heartbeat") return;
            var id = root.GetProperty("id").GetString() ?? "";
            if (string.IsNullOrEmpty(id)) return;
            var ip = root.GetProperty("ip").GetString() ?? "";
            // Lire les screens depuis le heartbeat
            var screens = new List<ScreenInfo>();
            if (root.TryGetProperty("screens", out var screensEl))
            {
                foreach (var s in screensEl.EnumerateArray())
                    screens.Add(new ScreenInfo {
                        Id     = s.TryGetProperty("id",     out var sid)  ? sid.GetString()  ?? "" : "",
                        Name   = s.TryGetProperty("name",   out var sn)   ? sn.GetString()   ?? "" : "",
                        Width  = s.TryGetProperty("width",  out var sw)   ? sw.GetInt32()  : 0,
                        Height = s.TryGetProperty("height", out var sh)   ? sh.GetInt32()  : 0,
                        Hz     = s.TryGetProperty("hz",     out var shz)  ? shz.GetInt32() : 0,
                    });
            }
            _postes[id] = new PosteEntry
            {
                Id = id,
                Name = root.GetProperty("name").GetString() ?? "",
                Ip = ip,
                Os = root.GetProperty("os").GetString() ?? "",
                LastSeen = DateTimeOffset.UtcNow,
                Screens = screens
            };
        }
        catch { }
    }

    public static List<PosteEntry> GetOnline()
    {
        var threshold = DateTimeOffset.UtcNow.AddSeconds(-TimeoutSeconds);
        return _postes.Values
            .Where(p => p.LastSeen >= threshold)
            .OrderBy(p => p.Name)
            .ToList();
    }

    public static List<PosteEntry> GetAll()
    {
        return _postes.Values.OrderBy(p => p.Name).ToList();
    }
}

public class PosteEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Os { get; set; } = "";
    public DateTimeOffset LastSeen { get; set; }
    public bool IsOnline => DateTimeOffset.UtcNow.AddSeconds(-15) <= LastSeen;
    public List<ScreenInfo> Screens { get; set; } = new();
}



