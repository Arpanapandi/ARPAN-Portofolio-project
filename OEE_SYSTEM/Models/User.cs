namespace OeeSystem.Models;

public enum UserRole
{
    Admin,
    Manager,
    Operator
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Operator;
    public string? ProfileImageUrl { get; set; }

    public ICollection<JobRun> JobRuns { get; set; } = new List<JobRun>();
}


