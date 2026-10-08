using SistemPacking.Web.Helpers;
using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;
using SistemPacking.Web.Data;

namespace SistemPacking.Web.Repositories;

public class GenericRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(int id)
        => await _dbSet.FirstOrDefaultAsync(x => x.Id == id);

    public virtual async Task<IEnumerable<T>> GetAllAsync()
        => await _dbSet.ToListAsync();

    public virtual async Task<T> AddAsync(T entity)
    {
        entity.CreatedAt = DateTime.Now;
        await _dbSet.AddAsync(entity);
        return entity;
    }

    public virtual async Task UpdateAsync(T entity)
    {
        entity.UpdatedAt = DateTime.Now;
        _dbSet.Update(entity);
        await Task.CompletedTask;
    }

    public virtual async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.Now;
            _dbSet.Update(entity);
        }
    }

    public virtual async Task<bool> ExistsAsync(int id)
        => await _dbSet.AnyAsync(x => x.Id == id);
}

// User Repository
public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context) { }

    public async Task<User?> GetByNIKAsync(string nik)
        => await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.NIK == nik);

    public async Task<User?> GetByUsernameAsync(string username)
        => await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == username);

    public async Task<IEnumerable<User>> GetWithRolesAsync()
        => await _context.Users.Include(u => u.Role).OrderBy(u => u.FullName).ToListAsync();

    public async Task<User?> GetWithRoleAsync(int id)
        => await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
}

// Order Repository
public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<Order>> GetAllWithDetailsAsync()
        => await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Item)
            .Include(o => o.ReleasedBy)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<Order?> GetWithDetailsAsync(int id)
        => await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Item).ThenInclude(i => i.FGLocation)
            .Include(o => o.OrderDetails).ThenInclude(od => od.ShoppingLogs)
            .Include(o => o.OrderDetails).ThenInclude(od => od.PackingLogs)
            .Include(o => o.ReleasedBy)
            .Include(o => o.Verification)
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status)
        => await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails)
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<Order>> GetByCustomerAsync(int customerId)
        => await _context.Orders
            .Include(o => o.Customer)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

    public async Task<string> GenerateOrderNoAsync()
    {
        var today = DateTime.Now;
        var prefix = $"ORD-{today:yyyyMMdd}-";
        var lastOrder = await _context.Orders
            .Where(o => o.OrderNo.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNo)
            .FirstOrDefaultAsync();

        int seq = 1;
        if (lastOrder != null)
        {
            var parts = lastOrder.OrderNo.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int lastSeq))
                seq = lastSeq + 1;
        }
        return $"{prefix}{seq:D4}";
    }
}

// OrderDetail Repository
public class OrderDetailRepository : GenericRepository<OrderDetail>, IOrderDetailRepository
{
    public OrderDetailRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<OrderDetail>> GetByOrderAsync(int orderId)
        => await _context.OrderDetails
            .Include(od => od.Item).ThenInclude(i => i.FGLocation)
            .Include(od => od.ShoppingLogs)
            .Include(od => od.PackingLogs)
            .Where(od => od.OrderId == orderId)
            .ToListAsync();

    public async Task<OrderDetail?> GetWithItemAsync(int id)
        => await _context.OrderDetails
            .Include(od => od.Item).ThenInclude(i => i.Customer)
            .Include(od => od.Item).ThenInclude(i => i.FGLocation)
            .Include(od => od.Order)
            .FirstOrDefaultAsync(od => od.Id == id);
}

// Item Repository
public class ItemRepository : GenericRepository<Item>, IItemRepository
{
    public ItemRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Item?> GetByBarcodeAsync(string barcode)
        => await _context.Items
            .Include(i => i.Customer)
            .Include(i => i.FGLocation)
            .FirstOrDefaultAsync(i => i.Barcode == barcode || i.ItemCode == barcode);

    public async Task<Item?> GetByCodeAsync(string code)
        => await _context.Items
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.ItemCode == code);

    public async Task<Item?> GetByCodeIncludeDeletedAsync(string code)
        => await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.ItemCode == code);

    public async Task<IEnumerable<Item>> GetByCustomerAsync(int customerId)
        => await _context.Items
            .Include(i => i.FGLocation)
            .Where(i => i.CustomerId == customerId)
            .ToListAsync();

    public async Task<IEnumerable<Item>> GetWithDetailsAsync()
        => await _context.Items
            .Include(i => i.Customer)
            .Include(i => i.FGLocation).ThenInclude(l => l!.Area)
            .OrderBy(i => i.ItemCode)
            .ToListAsync();

    public async Task<IEnumerable<Item>> GetAllIncludeDeletedAsync()
        => await _context.Items.IgnoreQueryFilters().ToListAsync();
}

// ShoppingLog Repository
public class ShoppingLogRepository : GenericRepository<ShoppingLog>, IShoppingLogRepository
{
    public ShoppingLogRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<ShoppingLog>> GetByOrderDetailAsync(int orderDetailId)
        => await _context.ShoppingLogs
            .Include(sl => sl.Operator)
            .Include(sl => sl.Shift)
            .Where(sl => sl.OrderDetailId == orderDetailId)
            .OrderByDescending(sl => sl.ScannedAt)
            .ToListAsync();

    public async Task<IEnumerable<ShoppingLog>> GetByOperatorAsync(int operatorId, DateTime date)
        => await _context.ShoppingLogs
            .Include(sl => sl.OrderDetail).ThenInclude(od => od.Item)
            .Where(sl => sl.OperatorId == operatorId && sl.ScannedAt.Date == date.Date)
            .OrderByDescending(sl => sl.ScannedAt)
            .ToListAsync();

    public async Task<decimal> GetTotalScannedAsync(int orderDetailId)
    {
        return await _dbSet.Where(l => l.OrderDetailId == orderDetailId && l.IsValid).SumAsync(l => l.ScannedQty);
    }
}

// PackingLog Repository
public class PackingLogRepository : GenericRepository<PackingLog>, IPackingLogRepository
{
    public PackingLogRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<PackingLog>> GetByOrderDetailAsync(int orderDetailId)
        => await _context.PackingLogs
            .Include(pl => pl.Operator)
            .Include(pl => pl.Shift)
            .Where(pl => pl.OrderDetailId == orderDetailId)
            .OrderByDescending(pl => pl.PackedAt)
            .ToListAsync();

    public async Task<IEnumerable<PackingLog>> GetByOperatorAsync(int operatorId, DateTime date)
        => await _context.PackingLogs
            .Include(pl => pl.OrderDetail).ThenInclude(od => od.Item)
            .Where(pl => pl.OperatorId == operatorId && pl.PackedAt.Date == date.Date)
            .OrderByDescending(pl => pl.PackedAt)
            .ToListAsync();

    public async Task<decimal> GetTotalPackedAsync(int orderDetailId)
        => await _context.PackingLogs
            .Where(pl => pl.OrderDetailId == orderDetailId)
            .SumAsync(pl => pl.PackedQty);
}

// ScanNgLog Repository
public class ScanNgLogRepository : GenericRepository<ScanNgLog>, IScanNgLogRepository
{
    public ScanNgLogRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<ScanNgLog>> GetByModuleAsync(string module)
        => await _context.ScanNgLogs
            .Include(sl => sl.Operator)
            .Where(sl => sl.Module == module)
            .OrderByDescending(sl => sl.ScannedAt)
            .ToListAsync();

    public async Task<IEnumerable<ScanNgLog>> GetByCategoryAsync(string category)
        => await _context.ScanNgLogs
            .Include(sl => sl.Operator)
            .Where(sl => sl.Category == category)
            .OrderByDescending(sl => sl.ScannedAt)
            .ToListAsync();
}

// Verification Repository
public class VerificationRepository : GenericRepository<Verification>, IVerificationRepository
{
    public VerificationRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Verification?> GetByOrderAsync(int orderId)
        => await _context.Verifications
            .Include(v => v.Leader)
            .Include(v => v.RejectReason)
            .FirstOrDefaultAsync(v => v.OrderId == orderId);

    public async Task<IEnumerable<Verification>> GetPendingAsync()
        => await _context.Verifications
            .Include(v => v.Order).ThenInclude(o => o.Customer)
            .Include(v => v.Leader)
            .Where(v => v.Status == VerificationStatus.Pending)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
}

// ActivityLog Repository
public class ActivityLogRepository : GenericRepository<ActivityLog>, IActivityLogRepository
{
    public ActivityLogRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IEnumerable<ActivityLog>> GetByUserAsync(int userId, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.ActivityLogs.Where(al => al.UserId == userId);
        if (from.HasValue) query = query.Where(al => al.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(al => al.CreatedAt <= to.Value);
        return await query.OrderByDescending(al => al.CreatedAt).ToListAsync();
    }

    public async Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 50)
        => await _context.ActivityLogs
            .Include(al => al.User)
            .OrderByDescending(al => al.CreatedAt)
            .Take(count)
            .ToListAsync();
}
