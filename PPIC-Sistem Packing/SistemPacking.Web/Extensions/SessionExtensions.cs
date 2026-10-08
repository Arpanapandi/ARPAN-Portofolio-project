using SistemPacking.Web.Models;
using System.Text.Json;

namespace SistemPacking.Web.Extensions;

public static class SessionExtensions
{
    private const string SessionKey = "UserSession";

    public static void SetUserSession(this ISession session, UserSessionDto user)
    {
        session.SetString(SessionKey, JsonSerializer.Serialize(user));
    }

    public static UserSessionDto? GetUserSession(this ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<UserSessionDto>(json); }
        catch { return null; }
    }

    public static void ClearUserSession(this ISession session)
    {
        session.Remove(SessionKey);
        session.Clear();
    }

    public static bool IsAuthenticated(this ISession session)
        => !string.IsNullOrEmpty(session.GetString(SessionKey));
}
