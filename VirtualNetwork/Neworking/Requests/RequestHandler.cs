using System.Text.Json;

namespace VirtualNetwork.Neworking.Requests
{
  public static class RequestHandler
  {
    private static readonly HttpClient _httpClient = new();

    public static async Task<HttpResponseMessage> MakeHttpRequest(string url, string token, params object[] data)
    {
      var request = new HttpRequestMessage(HttpMethod.Post, url);
      request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
      request.Content = new StringContent(JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json");

      return await _httpClient.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> MakeHttpRequest(string url, string token, Stream data)
    {
      var request = new HttpRequestMessage(HttpMethod.Post, url);
      request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
      request.Content = new StreamContent(data);
      request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

      return await _httpClient.SendAsync(request);
    }

    public static async Task<T> HandleResponse<T>(HttpResponseMessage response)
    {
      if (!response.IsSuccessStatusCode) throw new Exception($"Request failed with status code {response.StatusCode}");

      var responseContent = await response.Content.ReadAsStringAsync();
      return JsonSerializer.Deserialize<T>(responseContent) ?? throw new Exception("Failed to deserialize response");
    }
  }
}