namespace OeeSystem.Models;

public class ManPower
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // contoh: "1 Orang", "2 Orang", dll
    public int Value { get; set; }  // nilai numerik: 1, 2, 3, 4, 5
    public bool IsActive { get; set; } = true;
}

