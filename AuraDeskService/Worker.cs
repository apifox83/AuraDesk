using AuraDeskService.Network;
using AuraDeskService.Session;
using AuraDeskService.Mesh;

namespace AuraDeskService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private int _helperPid = -1;

    public Worker(ILogger<Worker> logger) { _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuraDesk Service démarré");
        LaunchHelper();

        // ── Watcher helper ────────────────────────────────────────────────
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken);
                if (_helperPid == -1 || !IsProcessRunning(_helperPid))
                {
                    _logger.LogInformation("Helper non actif, relancement...");
                    LaunchHelper();
                }
            }
        }, stoppingToken);

        // ── Couche réseau classique ───────────────────────────────────────
        var heartbeat = new UdpHeartbeat(_logger);
        var wsServer  = new WebSocketServer(_logger);

        // ── Couche Mesh ───────────────────────────────────────────────────
        var meshTable  = new MeshTable();
        var rawSocket  = new RawSocketListener(_logger);
        var meshProbe  = new MeshProbe(_logger, rawSocket, meshTable);
        var meshRouter = new MeshRouter(_logger, meshTable, rawSocket);

        // Câbler les notifications mesh → WebSocket broadcast
        meshTable.OnChanged = (payload) => wsServer.BroadcastMesh(payload);

        // Câbler la bascule de chemin → notification UI
        meshRouter.OnPathSwitch += (target, oldRelay, newRelay) =>
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new {
                type     = "mesh_path_switch",
                target,
                oldRelay,
                newRelay
            });
            wsServer.BroadcastMesh(payload);
        };

        // Câbler la découverte de postes silencieux → notification UI
        meshProbe.OnSilentNodeFound += (node) =>
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new {
                type     = "mesh_silent_found",
                mac      = node.Mac,
                hostname = node.Hostname,
                posteId  = node.PosteId,
                ip       = node.PhysicalIp,
                virtualIp= node.VirtualIp
            });
            wsServer.BroadcastMesh(payload);
        };

        await Task.WhenAll(
            heartbeat.StartAsync(stoppingToken),
            wsServer.StartAsync(stoppingToken),
            rawSocket.StartAsync(stoppingToken),
            meshProbe.StartAsync(stoppingToken),
            meshRouter.StartAsync(stoppingToken)
        );
    }

    private void LaunchHelper()
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "AuraDeskHelper.exe");
        if (!File.Exists(helperPath))
        {
            _logger.LogWarning("AuraDeskHelper.exe introuvable : {Path}", helperPath);
            return;
        }
        _helperPid = SessionHelper.LaunchInUserSession(helperPath);
        if (_helperPid > 0)
            _logger.LogInformation("Helper lancé (PID {Pid})", _helperPid);
        else
            _logger.LogWarning("Impossible de lancer le helper");
    }

    private static bool IsProcessRunning(int pid)
    {
        try { System.Diagnostics.Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AuraDesk Service arrêté");
        await base.StopAsync(cancellationToken);
    }
}

