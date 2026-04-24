using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualNetwork.Config
{
  public class AppConfig
  {
    [JsonPropertyName("middlemanUrl")]
    public string MiddlemanUrl { get; set; } = string.Empty;

    [JsonPropertyName("middlemanJwt")]
    public string MiddlemanJwt { get; set; } = string.Empty;

    [JsonPropertyName("isGateway")]
    public bool IsGateway { get; set; } = false;

    [JsonPropertyName("network")]
    public NetworkConfig Network { get; set; } = new NetworkConfig();

    public static AppConfig Load(string filePath)
    {
      var json = File.ReadAllText(filePath);
      return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }
  }

  public class NetworkConfig
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("addressMask")]
    public string AddressMask { get; set; } = string.Empty;

    [JsonPropertyName("gatewayId")]
    public string GatewayId { get; set; } = string.Empty;

    [JsonPropertyName("gatewayName")]
    public string GatewayName { get; set; } = string.Empty;
  }
}