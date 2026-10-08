using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SistemPacking.Web.Interfaces;

namespace SistemPacking.Web.Services;

public class DeliveryControlIntegrationService : IDeliveryControlIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeliveryControlIntegrationService> _logger;

    public DeliveryControlIntegrationService(HttpClient httpClient, IConfiguration config, ILogger<DeliveryControlIntegrationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        var baseUrl = config["DeliveryControlApi:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            if (System.Uri.TryCreate(baseUrl, System.UriKind.Absolute, out var uri))
            {
                _httpClient.BaseAddress = uri;
            }
            else
            {
                _logger.LogWarning($"DeliveryControlApi:BaseUrl '{baseUrl}' is not a valid URI.");
            }
        }
    }

    public async Task<DeliveryControlScanResponse> SendShoppingScanAsync(DeliveryControlScanRequest request)
    {
        try
        {
            if (_httpClient.BaseAddress == null)
            {
                _logger.LogWarning("DeliveryControlApi:BaseUrl is not configured.");
                return new DeliveryControlScanResponse { success = false, message = "API URL is not configured." };
            }

            var response = await _httpClient.PostAsJsonAsync("api/packing/scan-shopping", request);
            
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var result = await response.Content.ReadFromJsonAsync<DeliveryControlScanResponse>(jsonOptions);
                    return result ?? new DeliveryControlScanResponse { success = true };
                }
                catch (System.Text.Json.JsonException)
                {
                    var htmlContent = await response.Content.ReadAsStringAsync();
                    var preview = htmlContent.Length > 200 ? htmlContent.Substring(0, 200) + "..." : htmlContent;
                    return new DeliveryControlScanResponse { success = false, message = $"API returned HTML instead of JSON (Possible redirect to Login). Preview: {preview}" };
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to send shopping scan. Status code: {response.StatusCode}. Content: {errorContent}");

            try
            {
                var result = System.Text.Json.JsonSerializer.Deserialize<DeliveryControlScanResponse>(errorContent, jsonOptions);
                if (result != null && !string.IsNullOrEmpty(result.message))
                {
                    return result;
                }
            }
            catch { }

            return new DeliveryControlScanResponse { success = false, message = $"HTTP Error {(int)response.StatusCode}: {errorContent}" };
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error communicating with Delivery Control API.");
            return new DeliveryControlScanResponse { success = false, message = ex.Message };
        }
    }
}
