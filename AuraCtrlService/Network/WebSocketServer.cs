using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using AuraCtrlService.Network;
using AuraCtrlService.Screen;
using AuraCtrlService.Input;

namespace AuraCtrlService.Network;

public class WebSocketServer
{
    private readonly ILogger _logger;
    private const int Port = 47201;
    private static readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    private static bool _previewEnabled = true;
    private static int _remoteW = 1280;
    private static int _remoteH = 720;
    private static readonly string _token = Environment.GetEnvironmentVariable("AURA_TOKEN") ?? "";

    public WebSocketServer(ILogger logger) { _logger = logger; }

    public async Task StartAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Prefixes.Add($"http://localhost:{Port}/ws/");
        listener.Start();
        _logger.LogInformation("WebSocket server sur port {Port}", Port);
        var screenTask = ScreenStreamLoopAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try { var ctx = await listener.GetContextAsync(); _ = HandleContextAsync(ctx, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning("HTTP error: {Msg}", ex.Message); }
        }
        listener.Stop();
        await screenTask;
    }

    private async Task ScreenStreamLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_previewEnabled && _clients.Count > 0)
                {
                    var jpeg = ScreenCapture.CaptureScreen(quality: 35, maxWidth: 1280);
                    var b64  = Convert.ToBase64String(jpeg);
                    var msg  = JsonSerializer.Serialize(new { type = "screen_frame", data = b64 });
                    await BroadcastAsync(msg, ct);
                }
            }
            catch (Exception ex) { _logger.LogWarning("Screen capture: {Msg}", ex.Message); }
            await Task.Delay(100, ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_token))
        {
            var queryToken = ctx.Request.QueryString["token"] ?? "";
            if (queryToken != _token) { ctx.Response.StatusCode = 401; ctx.Response.Close(); return; }
        }
        if (ctx.Request.IsWebSocketRequest)
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var id = Guid.NewGuid().ToString();
            _clients[id] = wsCtx.WebSocket;
            _logger.LogInformation("Client connecté: {Id}", id);
            await HandleWebSocketAsync(id, wsCtx.WebSocket, ct);
            _clients.TryRemove(id, out _);
            _logger.LogInformation("Client déconnecté: {Id}", id);
        }
        else { await ServeStaticAsync(ctx); }
    }

    private async Task HandleWebSocketAsync(string clientId, WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct); break; }
                await ProcessMessageAsync(clientId, Encoding.UTF8.GetString(buffer, 0, result.Count), ct);
            }
            catch { break; }
        }
    }

    private async Task ProcessMessageAsync(string clientId, string json, CancellationToken ct)
    {
        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "get_postes":
                    var response = JsonSerializer.Serialize(new { type = "postes_list", data = PosteRegistry.GetOnline() });
                    await SendAsync(clientId, response, ct);
                    break;
                case "set_preview":
                    _previewEnabled = root.GetProperty("enabled").GetBoolean();
                    break;
                case "remote_resolution":
                    _remoteW = root.GetProperty("w").GetInt32();
                    _remoteH = root.GetProperty("h").GetInt32();
                    break;
                case "mouse_move":
                {
                    int rx = root.GetProperty("x").GetInt32();
                    int ry = root.GetProperty("y").GetInt32();
                    int sw = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width;
                    int sh = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Height;
                    int ax = (int)(rx * (sw / (double)Math.Max(_remoteW, 1)));
                    int ay = (int)(ry * (sh / (double)Math.Max(_remoteH, 1)));
                    InputInjector.MoveMouse(ax, ay);
                    break;
                }
                case "mouse_click":
                {
                    int rx  = root.GetProperty("x").GetInt32();
                    int ry  = root.GetProperty("y").GetInt32();
                    var btn = root.TryGetProperty("button", out var bel) ? bel.GetString() ?? "left" : "left";
                    bool dbl = root.TryGetProperty("double", out var del) && del.GetBoolean();
                    int sw  = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width;
                    int sh  = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Height;
                    int ax  = (int)(rx * (sw / (double)Math.Max(_remoteW, 1)));
                    int ay  = (int)(ry * (sh / (double)Math.Max(_remoteH, 1)));
                    InputInjector.MouseClick(ax, ay, btn, dbl);
                    break;
                }
                case "mouse_scroll":
                    InputInjector.MouseScroll(root.GetProperty("delta").GetInt32());
                    break;
                case "key_press":
                    InputInjector.KeyPress(root.GetProperty("vk").GetUInt16());
                    break;
                case "key_down":
                    InputInjector.KeyDown(root.GetProperty("vk").GetUInt16());
                    break;
                case "key_up":
                    InputInjector.KeyUp(root.GetProperty("vk").GetUInt16());
                    break;
                case "type_text":
                    InputInjector.TypeText(root.GetProperty("text").GetString() ?? "");
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
        var wwwroot  = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var reqPath  = ctx.Request.Url?.LocalPath.TrimStart('/') ?? "";
        if (string.IsNullOrEmpty(reqPath)) reqPath = "index.html";
        var filePath = Path.Combine(wwwroot, reqPath);
        if (File.Exists(filePath))
        {
            ctx.Response.ContentType = Path.GetExtension(filePath).ToLower() switch
            {
                ".html" => "text/html; charset=utf-8", ".js" => "application/javascript",
                ".css"  => "text/css; charset=utf-8",  _     => "application/octet-stream"
            };
            var content = await File.ReadAllBytesAsync(filePath);
            ctx.Response.ContentLength64 = content.Length;
            await ctx.Response.OutputStream.WriteAsync(content);
        }
        else { ctx.Response.StatusCode = 404; }
        ctx.Response.Close();
    }
}

