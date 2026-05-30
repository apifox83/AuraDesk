using AuraDeskService;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => { options.ServiceName = "AuraDeskService"; })
    .ConfigureServices(services => { services.AddHostedService<Worker>(); });

builder.Build().Run();
