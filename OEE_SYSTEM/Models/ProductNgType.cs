namespace OeeSystem.Models;

public class ProductNgType
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    
    public int NgTypeId { get; set; }
    public NgType? NgType { get; set; }
}

