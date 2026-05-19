using System.Net;
using System.Net.WebSockets;
using System.Text;
using AuraCtrlService.Network;

namespace AuraCtrlService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private UdpHeartbeat? _heartbeat;
    private WebSocketServer? _wsServer;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuraCtrl Service démarré");

        _heartbeat = new UdpHeartbeat(_logger);
        _wsServer = new WebSocketServer(_logger);

        var heartbeatTask = _heartbeat.StartAsync(stoppingToken);
        var wsTask = _wsServer.StartAsync(stoppingToken);

        await Task.WhenAll(heartbeatTask, wsTask);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AuraCtrl Service arrêté");
        await base.StopAsync(cancellationToken);
    }
}
