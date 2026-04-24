using System.Reflection;
using MiddleManClient.ConnectionBuilder;
using VirtualNetwork.Config;
using VirtualNetwork.Neworking;
using VirtualNetwork.VirtualAdapter;

namespace VirtualNetwork;

class Program
{
  static async Task Main(string[] args)
  {
    var configPath = GetConfigPath(args);
    var config = AppConfig.Load(configPath);

    var router = new Router(config);
    var adapter = new WindowsNetworkAdapter(router);
    
    StartMiddleManClient(config, adapter, router);
    
    Console.WriteLine($"Starting virtual network client. Gateway mode: {config.IsGateway}");
    await adapter.Start();
  }

  private static void StartMiddleManClient(AppConfig config, IVirtualNetworkAdapter adapter, Router router)
  {
    var serverThread = new Thread(async () =>
    {
      var connection = ClientConnectioBuilderFactory.Create()
       .WithHost($"{config.MiddlemanUrl}/playground")
       .WithToken(config.MiddlemanJwt)
       .WithReconnect()
       .Build();

      await connection.UseAssembly(Assembly.GetExecutingAssembly())
        .RequestHttpMetadata(true)
        .AddMethodCallingHandler(adapter)
        .AddMethodCallingHandler(router)
        .StartAsync();
    })
    {
      IsBackground = true
    };
    serverThread.Start();
  }

  private static string GetConfigPath(string[] args)
  {
    var envPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
    if (!string.IsNullOrEmpty(envPath)) return envPath;

    if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) return args[0];

    return "D:\\Documents\\Projects\\MiddleMan Services\\VirtualNetwork\\config.json";
  }
}
