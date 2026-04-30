namespace Signage.Server.Data;

public enum AdminUserRole
{
    User = 0,
    Admin = 1
}

public sealed class AdminUser
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public AdminUserRole Role { get; set; } = AdminUserRole.Admin;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
