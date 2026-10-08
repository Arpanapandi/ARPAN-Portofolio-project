using System.Threading.Tasks;

namespace SistemPacking.Web.Interfaces;

public class DeliveryControlScanRequest
{
    public string tag { get; set; } = string.Empty;
    public string label { get; set; } = string.Empty;
    public string? kanban { get; set; }
    public string? user { get; set; }
}

public class DeliveryControlScanResponse
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
}

public interface IDeliveryControlIntegrationService
{
    Task<DeliveryControlScanResponse> SendShoppingScanAsync(DeliveryControlScanRequest request);
}
