using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace AuraCtrlService.Network;

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
            _postes[id] = new PosteEntry
            {
                Id = id,
                Name = root.GetProperty("name").GetString() ?? "",
                Ip = root.GetProperty("ip").GetString() ?? "",
                Os = root.GetProperty("os").GetString() ?? "",
                LastSeen = DateTimeOffset.UtcNow
            };
        }
        catch { }
    }

    public static List<PosteEntry> GetOnline()
    {
        var threshold = DateTimeOffset.UtcNow.AddSeconds(-TimeoutSeconds);
        return _postes.Values
            .Where(p => p.LastSeen >= threshold && !_localIps.Contains(p.Ip))
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
}
