namespace OeeSystem.Models;

public class Plant
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // contoh: PLT01
    public string Name { get; set; } = string.Empty; // contoh: Plant Cikarang

    public ICollection<Machine> Machines { get; set; } = new List<Machine>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}



