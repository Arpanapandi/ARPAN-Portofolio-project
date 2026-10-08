namespace OeeSystem.Models;

public class NgType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;      // contoh: NG01
    public string Name { get; set; } = string.Empty;      // contoh: Burr
    public string Category { get; set; } = string.Empty;  // contoh: Visual / Dimensi (opsional)
    public string? Description { get; set; }              // keterangan detail (opsional)

    // Mapping tipe NG ke produk yang relevan
    public ICollection<ProductNgType> ProductNgTypes { get; set; } = new List<ProductNgType>();
}

