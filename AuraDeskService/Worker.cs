using AuraDeskService.Network;

namespace AuraDeskService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) { _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuraDesk Service démarré");

        var heartbeat = new UdpHeartbeat(_logger);
        var wsServer  = new WebSocketServer(_logger);

        var heartbeatTask = heartbeat.StartAsync(stoppingToken);
        var wsTask        = wsServer.StartAsync(stoppingToken);

        await Task.WhenAll(heartbeatTask, wsTask);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AuraDesk Service arrêté");
        await base.StopAsync(cancellationToken);
    }
}
