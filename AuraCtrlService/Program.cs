using AuraCtrlService;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => { options.ServiceName = "AuraCtrlService"; })
    .ConfigureServices(services => { services.AddHostedService<Worker>(); });

builder.Build().Run();
