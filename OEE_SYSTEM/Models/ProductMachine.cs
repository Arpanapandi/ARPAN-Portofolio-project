namespace OeeSystem.Models;

public class ProductMachine
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string MachineId { get; set; } = string.Empty;
    public Machine? Machine { get; set; }
}



