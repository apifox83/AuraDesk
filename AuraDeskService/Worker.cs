using AuraDeskService.Network;
using AuraDeskService.Session;

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

        var heartbeat = new UdpHeartbeat(_logger);
        var wsServer  = new WebSocketServer(_logger);

        await Task.WhenAll(heartbeat.StartAsync(stoppingToken), wsServer.StartAsync(stoppingToken));
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
