using SistemPacking.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace SistemPacking.Web.Models;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
