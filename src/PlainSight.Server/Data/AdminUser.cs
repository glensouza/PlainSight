using System.ComponentModel.DataAnnotations;

namespace PlainSight.Server.Data;

public enum AdminUserRole
{
    User = 0,
    Admin = 1
}

public sealed class AdminUser
{
    public int Id { get; init; }

    [MaxLength(100)]
    public required string Username { get; init; }

    [MaxLength(200)]
    public required string PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;
    public AdminUserRole Role { get; set; } = AdminUserRole.Admin;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
