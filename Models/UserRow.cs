using KestrelScope.Constants;

namespace KestrelScope.Models;

public class UserRow
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = AppConstants.UserRoles.Standard;
    public string? CreatedAt { get; set; }
}

