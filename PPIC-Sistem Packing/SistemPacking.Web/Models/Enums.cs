using SistemPacking.Web.Models;
namespace SistemPacking.Web.Models;

public enum OrderStatus
{
    Draft = 0,
    WaitingShopping = 1,
    Shopping = 2,
    ShoppingComplete = 3,
    ReadyPacking = 4,
    Packing = 5,
    PackingComplete = 6,
    WaitingVerification = 7,
    Approved = 8,
    Rejected = 9,
    Cancelled = 10,
    Finished = 11
}

public enum UserRole
{
    SuperAdmin = 1,
    Leader = 2,
    OperatorShopping = 3,
    OperatorPacking = 4,
    Viewer = 5
}

public enum ItemStatus
{
    Active = 1,
    Inactive = 0
}

public enum VerificationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum ShiftType
{
    Morning = 1,
    Afternoon = 2,
    Night = 3
}
