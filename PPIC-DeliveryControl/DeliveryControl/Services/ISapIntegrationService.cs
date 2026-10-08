using DeliveryControl.Models;
using DeliveryControl.Models.DTOs;
using DeliveryControl.Models.ViewModels;

namespace DeliveryControl.Services
{
    public interface ISapIntegrationService
    {
        Task<DeliverySchedule> FetchSoAndConvertToOdAsync(string soNumber, string url, string headerKey, string headerValue, string shippingPoint);
        Task<SapSalesOrderDto> FetchSoDetailsAsync(string soNumber, string url, string headerKey, string headerValue, string shippingPoint);
        Task<DeliverySchedule> SaveCreateODAsync(CreateOrderDeliveryViewModel model);
    }
}
