using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AuraCtrlService.Network;

public class WebSocketServer
{
    private readonly ILogger _logger;
    private const int Port = 47201;
    private static readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    public WebSocketServer(ILogger logger) { _logger = logger; }

    public async Task StartAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Prefixes.Add($"http://localhost:{Port}/ws/");
        listener.Start();
        _logger.LogInformation("WebSocket server sur port {Port}", Port);
        while (!ct.IsCancellationRequested)
        {
            try { var ctx = await listener.GetContextAsync(); _ = HandleContextAsync(ctx, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning("HTTP error: {Msg}", ex.Message); }
        }
        listener.Stop();
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (ctx.Request.IsWebSocketRequest)
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var id = Guid.NewGuid().ToString();
            _clients[id] = wsCtx.WebSocket;
            await HandleWebSocketAsync(id, wsCtx.WebSocket, ct);
            _clients.TryRemove(id, out _);
        }
        else { await ServeStaticAsync(ctx); }
    }

    private async Task HandleWebSocketAsync(string clientId, WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct); break; }
                await ProcessMessageAsync(clientId, Encoding.UTF8.GetString(buffer, 0, result.Count), ct);
            }
            catch { break; }
        }
    }

    private async Task ProcessMessageAsync(string clientId, string json, CancellationToken ct)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "get_postes":
                    var response = JsonSerializer.Serialize(new { type = "postes_list", data = PosteRegistry.GetOnline() });
                    await SendAsync(clientId, response, ct);
                    break;
                default:
                    await BroadcastAsync(json, ct);
                    break;
            }
        }
        catch (Exception ex) { _logger.LogWarning("ProcessMessage: {Msg}", ex.Message); }
    }

    private async Task SendAsync(string clientId, string json, CancellationToken ct)
    {
        if (_clients.TryGetValue(clientId, out var ws) && ws.State == WebSocketState.Open)
        {
            var data = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(data, WebSocketMessageType.Text, true, ct);
        }
    }

    private async Task BroadcastAsync(string json, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(json);
        foreach (var kv in _clients)
            try { if (kv.Value.State == WebSocketState.Open) await kv.Value.SendAsync(data, WebSocketMessageType.Text, true, ct); } catch { }
    }

    private static async Task ServeStaticAsync(HttpListenerContext ctx)
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var reqPath = ctx.Request.Url?.LocalPath.TrimStart('/') ?? "";
        if (string.IsNullOrEmpty(reqPath)) reqPath = "index.html";
        var filePath = Path.Combine(wwwroot, reqPath);
        if (File.Exists(filePath))
        {
            ctx.Response.ContentType = Path.GetExtension(filePath).ToLower() switch { ".html" => "text/html", ".js" => "application/javascript", ".css" => "text/css", _ => "application/octet-stream" };
            var content = await File.ReadAllBytesAsync(filePath);
            ctx.Response.ContentLength64 = content.Length;
            await ctx.Response.OutputStream.WriteAsync(content);
        }
        else { ctx.Response.StatusCode = 404; }
        ctx.Response.Close();
    }
}
