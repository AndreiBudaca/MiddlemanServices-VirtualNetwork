using VirtualNetwork.Config;

namespace VirtualNetwork;

class Program
{
  static void Main(string[] args)
  {
    var configPath = GetConfigPath(args);
    var config = AppConfig.Load(configPath);
  }

  private static string GetConfigPath(string[] args)
  {
    var envPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
    if (!string.IsNullOrEmpty(envPath)) return envPath;

    if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) return args[0];

    return "D:\\Documents\\Projects\\MiddleMan Services\\VirtualNetwork\\config.json";
  }
}
