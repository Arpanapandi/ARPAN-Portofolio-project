namespace OeeSystem.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string UoM { get; set; } = string.Empty; // Unit of Measure
    public string SLOC { get; set; } = string.Empty; // Storage Location
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    public string? ImageUrl { get; set; }

    // Standar cycle time per produk (detik)
    public double StandarCycleTime { get; set; }

    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();

    // Mapping produk ke mesin yang diizinkan
    public ICollection<ProductMachine> ProductMachines { get; set; } = new List<ProductMachine>();

    // Mapping produk ke tipe NG yang relevan
    public ICollection<ProductNgType> ProductNgTypes { get; set; } = new List<ProductNgType>();
}


