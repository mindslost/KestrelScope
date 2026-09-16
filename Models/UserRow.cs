namespace KestrelScope.Models;

public class UserRow
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "standard";
    public string? CreatedAt { get; set; }
}

