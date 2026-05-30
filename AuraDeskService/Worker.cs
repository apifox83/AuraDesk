using AuraDeskService.Network;
using AuraDeskService.Session;

namespace AuraDeskService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private UdpHeartbeat? _heartbeat;
    private WebSocketServer? _wsServer;
    private int _helperPid = -1;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuraDesk Service démarré");

        // Lancer le helper dans la session utilisateur interactive
        LaunchHelper();

        // Surveiller le helper et le relancer si nécessaire
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

        _heartbeat = new UdpHeartbeat(_logger);
        _wsServer  = new WebSocketServer(_logger);

        var heartbeatTask = _heartbeat.StartAsync(stoppingToken);
        var wsTask        = _wsServer.StartAsync(stoppingToken);

        await Task.WhenAll(heartbeatTask, wsTask);
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
            _logger.LogInformation("Helper lancé dans la session utilisateur (PID {Pid})", _helperPid);
        else
            _logger.LogWarning("Impossible de lancer le helper dans la session utilisateur");
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
