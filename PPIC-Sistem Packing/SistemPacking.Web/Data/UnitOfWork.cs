using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;
using SistemPacking.Web.Data;
using SistemPacking.Web.Repositories;

namespace SistemPacking.Web.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public IUserRepository Users { get; }
    public IRepository<Role> Roles { get; }
    public IRepository<Customer> Customers { get; }
    public IRepository<Area> Areas { get; }
    public IRepository<FGLocation> FGLocations { get; }
    public IItemRepository Items { get; }
    public IRepository<Shift> Shifts { get; }
    public IRepository<ReasonReject> ReasonRejects { get; }
    public IOrderRepository Orders { get; }
    public IOrderDetailRepository OrderDetails { get; }
    public IRepository<OrderUploadHistory> OrderUploadHistories { get; }
    public IShoppingLogRepository ShoppingLogs { get; }
    public IPackingLogRepository PackingLogs { get; }
    public IScanNgLogRepository ScanNgLogs { get; }
    public IVerificationRepository Verifications { get; }
    public IActivityLogRepository ActivityLogs { get; }
    public IRepository<ItemMapping> ItemMappings { get; }
    public IRepository<ManPower> ManPowers { get; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
        Users = new UserRepository(context);
        Roles = new GenericRepository<Role>(context);
        Customers = new GenericRepository<Customer>(context);
        Areas = new GenericRepository<Area>(context);
        FGLocations = new GenericRepository<FGLocation>(context);
        Items = new ItemRepository(context);
        Shifts = new GenericRepository<Shift>(context);
        ReasonRejects = new GenericRepository<ReasonReject>(context);
        Orders = new OrderRepository(context);
        OrderDetails = new OrderDetailRepository(context);
        OrderUploadHistories = new GenericRepository<OrderUploadHistory>(context);
        ShoppingLogs = new ShoppingLogRepository(context);
        PackingLogs = new PackingLogRepository(context);
        ScanNgLogs = new ScanNgLogRepository(context);
        Verifications = new VerificationRepository(context);
        ActivityLogs = new ActivityLogRepository(context);
        ItemMappings = new GenericRepository<ItemMapping>(context);
        ManPowers = new GenericRepository<ManPower>(context);
    }

    public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

    public void Dispose() => _context.Dispose();
}
