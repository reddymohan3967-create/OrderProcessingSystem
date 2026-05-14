using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;

namespace OrderService.Interfaces;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderResponse?> GetOrderByIdAsync(Guid id);
    Task<List<OrderResponse>> GetOrdersAsync(OrderStatus? status = null);
    Task<bool> CancelOrderAsync(Guid id);
    Task<bool> UpdateOrderStatusAsync(Guid id, OrderStatus newStatus, bool force = false);
}
