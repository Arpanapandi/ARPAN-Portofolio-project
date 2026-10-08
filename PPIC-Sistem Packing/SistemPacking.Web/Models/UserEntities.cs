using SistemPacking.Web.Models;

namespace SistemPacking.Web.Models;

public class Role : BaseEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool CanManageMaster { get; set; } = false;
    public bool CanManageOrder { get; set; } = false;
    public bool CanShopping { get; set; } = false;
    public bool CanPacking { get; set; } = false;
    public bool CanVerify { get; set; } = false;
    public bool CanViewReport { get; set; } = false;
    public bool CanManageUser { get; set; } = false;
    
    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User : BaseEntity
{
    public string NIK { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIP { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
    public ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();
}

public class ActivityLog : BaseEntity
{
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Module { get; set; }

    // Navigation
    public User? User { get; set; }
}
