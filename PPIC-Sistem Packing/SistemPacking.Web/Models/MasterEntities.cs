using SistemPacking.Web.Models;

namespace SistemPacking.Web.Models;

public class Customer : BaseEntity
{
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    public string? Dock { get; set; }
    public string? Route { get; set; }
    public string? Cycle { get; set; }
    public string? StdShopping { get; set; }
    public string? StdPacking { get; set; }
    public string? StdPickup { get; set; }

    // Navigation
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Area : BaseEntity
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public ICollection<FGLocation> Locations { get; set; } = new List<FGLocation>();
}

public class FGLocation : BaseEntity
{
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int AreaId { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Area Area { get; set; } = null!;
    public ICollection<Item> Items { get; set; } = new List<Item>();
}

public class Item : BaseEntity
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string UOM { get; set; } = string.Empty;
    public decimal PackingStandard { get; set; }
    public int? FGLocationId { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Active;
    public string? Description { get; set; }
    public string? SpisImagePath { get; set; }
    public string? SppsImagePath { get; set; }
    public string? VIN { get; set; }
    public string? Dock { get; set; }
    public string? Point1 { get; set; }
    public string? Point2 { get; set; }
    public string? Point3 { get; set; }
    public string? Point4 { get; set; }
    public string? Point5 { get; set; }
    public string? TypeKarton { get; set; }
    public string? TypePlastik { get; set; }
    public string Category { get; set; } = "Export";

    // New fields for Master Item (Combined)
    public string? ProdPlant { get; set; }
    public string? LokasiRack { get; set; }
    public string? Rack { get; set; }
    public string? NoRack { get; set; }
    public decimal QtyPcs { get; set; }
    public decimal ActQty { get; set; }
    public decimal Min { get; set; }
    public decimal Rop { get; set; }
    public decimal Max { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public FGLocation? FGLocation { get; set; }
    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}

public class Shift : BaseEntity
{
    public string ShiftName { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ReasonReject : BaseEntity
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class ItemMapping : BaseEntity
{
    public int ItemId { get; set; }
    public string KanbanCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Item Item { get; set; } = null!;
}

public class ManPower : BaseEntity
{
    public string NPK { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
