namespace KestrelScope.Models;

public class UserDto
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "standard";
    public string? CreatedAt { get; set; }
}

