using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;

namespace SistemPacking.Web.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, UserSessionDto? User)> LoginAsync(LoginDto dto, string ipAddress, string userAgent);
    Task LogoutAsync(int userId, string ipAddress);
}

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _uow;

    public AuthService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<(bool Success, string Message, UserSessionDto? User)> LoginAsync(LoginDto dto, string ipAddress, string userAgent)
    {
        // Find by NIK or Username
        var user = await _uow.Users.GetByNIKAsync(dto.NIK)
                   ?? await _uow.Users.GetByUsernameAsync(dto.NIK);

        if (user == null)
            return (false, "NIK/Username tidak ditemukan.", null);

        if (!user.IsActive)
            return (false, "Akun tidak aktif. Hubungi administrator.", null);

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return (false, "Password salah.", null);

        // Update last login
        user.LastLoginAt = DateTime.Now;
        user.LastLoginIP = ipAddress;
        await _uow.Users.UpdateAsync(user);

        // Log activity
        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = user.Id,
            Action = "LOGIN",
            Description = $"User {user.NIK} login berhasil",
            IPAddress = ipAddress,
            UserAgent = userAgent,
            Module = "Auth"
        });

        await _uow.SaveChangesAsync();

        var session = new UserSessionDto
        {
            Id = user.Id,
            NIK = user.NIK,
            Username = user.Username,
            FullName = user.FullName,
            RoleId = user.RoleId,
            RoleName = user.Role.RoleName,
            CanManageMaster = user.Role.CanManageMaster,
            CanManageOrder = user.Role.CanManageOrder,
            CanShopping = user.Role.CanShopping,
            CanPacking = user.Role.CanPacking,
            CanVerify = user.Role.CanVerify,
            CanViewReport = user.Role.CanViewReport,
            CanManageUser = user.Role.CanManageUser
        };

        return (true, "Login berhasil.", session);
    }

    public async Task LogoutAsync(int userId, string ipAddress)
    {
        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = userId,
            Action = "LOGOUT",
            Description = $"User logout",
            IPAddress = ipAddress,
            Module = "Auth"
        });
        await _uow.SaveChangesAsync();
    }
}
