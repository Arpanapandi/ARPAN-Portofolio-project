using SistemPacking.Web.Models;

namespace SistemPacking.Web.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByNIKAsync(string nik);
    Task<User?> GetByUsernameAsync(string username);
    Task<IEnumerable<User>> GetWithRolesAsync();
    Task<User?> GetWithRoleAsync(int id);
}

public interface IOrderRepository : IRepository<Order>
{
    Task<IEnumerable<Order>> GetAllWithDetailsAsync();
    Task<Order?> GetWithDetailsAsync(int id);
    Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status);
    Task<IEnumerable<Order>> GetByCustomerAsync(int customerId);
    Task<string> GenerateOrderNoAsync();
}

public interface IOrderDetailRepository : IRepository<OrderDetail>
{
    Task<IEnumerable<OrderDetail>> GetByOrderAsync(int orderId);
    Task<OrderDetail?> GetWithItemAsync(int id);
}

public interface IItemRepository : IRepository<Item>
{
    Task<Item?> GetByBarcodeAsync(string barcode);
    Task<Item?> GetByCodeAsync(string code);
    Task<Item?> GetByCodeIncludeDeletedAsync(string code);
    Task<IEnumerable<Item>> GetByCustomerAsync(int customerId);
    Task<IEnumerable<Item>> GetWithDetailsAsync();
    Task<IEnumerable<Item>> GetAllIncludeDeletedAsync();
}

public interface IShoppingLogRepository : IRepository<ShoppingLog>
{
    Task<IEnumerable<ShoppingLog>> GetByOrderDetailAsync(int orderDetailId);
    Task<IEnumerable<ShoppingLog>> GetByOperatorAsync(int operatorId, DateTime date);
    Task<decimal> GetTotalScannedAsync(int orderDetailId);
}

public interface IPackingLogRepository : IRepository<PackingLog>
{
    Task<IEnumerable<PackingLog>> GetByOrderDetailAsync(int orderDetailId);
    Task<IEnumerable<PackingLog>> GetByOperatorAsync(int operatorId, DateTime date);
    Task<decimal> GetTotalPackedAsync(int orderDetailId);
}

public interface IScanNgLogRepository : IRepository<ScanNgLog>
{
    Task<IEnumerable<ScanNgLog>> GetByModuleAsync(string module);
    Task<IEnumerable<ScanNgLog>> GetByCategoryAsync(string category);
}

public interface IVerificationRepository : IRepository<Verification>
{
    Task<Verification?> GetByOrderAsync(int orderId);
    Task<IEnumerable<Verification>> GetPendingAsync();
}

public interface IActivityLogRepository : IRepository<ActivityLog>
{
    Task<IEnumerable<ActivityLog>> GetByUserAsync(int userId, DateTime? from = null, DateTime? to = null);
    Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 50);
}

public interface IUnitOfWork : IDisposable
{
    IUserRepository Users { get; }
    IRepository<Role> Roles { get; }
    IRepository<Customer> Customers { get; }
    IRepository<Area> Areas { get; }
    IRepository<FGLocation> FGLocations { get; }
    IItemRepository Items { get; }
    IRepository<Shift> Shifts { get; }
    IRepository<ReasonReject> ReasonRejects { get; }
    IOrderRepository Orders { get; }
    IOrderDetailRepository OrderDetails { get; }
    IRepository<OrderUploadHistory> OrderUploadHistories { get; }
    IShoppingLogRepository ShoppingLogs { get; }
    IPackingLogRepository PackingLogs { get; }
    IScanNgLogRepository ScanNgLogs { get; }
    IVerificationRepository Verifications { get; }
    IActivityLogRepository ActivityLogs { get; }
    IRepository<ItemMapping> ItemMappings { get; }
    IRepository<ManPower> ManPowers { get; }

    Task<int> SaveChangesAsync();
}
