using System.Reflection;
using System.Runtime.InteropServices;
using MiddleManClient;
using MiddleManClient.ConnectionBuilder;
using MiddleManClient.MethodProcessing.MethodFunctionHandlerGenerator;
using VirtualNetwork.Config;
using VirtualNetwork.Context;
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
    var adapter = CreateAdapter(router);

    ConnectionContext.Connection = await StartMiddleManClient(config, adapter, router);

    Console.WriteLine($"Starting virtual network client. Gateway mode: {config.IsGateway}");
    await adapter.Start();
  }

  private static IVirtualNetworkAdapter CreateAdapter(Router router)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return new WindowsNetworkAdapter(router);
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return new LinuxNetworkAdapter(router);
    }

    throw new PlatformNotSupportedException("Only Windows and Linux are supported by VirtualNetwork adapters.");
  }

  private async static Task<ClientConnection> StartMiddleManClient(AppConfig config, IVirtualNetworkAdapter adapter, Router router)
  {
    var connection = ClientConnectioBuilderFactory.Create()
     .WithHost($"{config.MiddlemanUrl}/playground")
     .WithToken(config.MiddlemanJwt)
     .WithReconnect()
     .WithPoolSize(5)
     .Build();

    await connection.UseAssembly(Assembly.GetExecutingAssembly())
      .UseMethodFunctionHandlerGenerator(new DirectInvocationFunctionHandlerGenerator())
      .AddMethodCallingHandler(adapter)
      .AddMethodCallingHandler(router)
      .StartAsync(false);

    return connection;
  }

  private static string GetConfigPath(string[] args)
  {
    var envPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
    if (!string.IsNullOrEmpty(envPath)) return envPath;

    if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) return args[0];

    return "D:\\Documents\\Projects\\MiddleMan Services\\VirtualNetwork\\config.json";
  }
}
