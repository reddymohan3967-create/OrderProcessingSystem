using System.ComponentModel.DataAnnotations;

namespace OrderService.Entities;

/// <summary>
/// Represents an application user. For simplicity the entity stores
/// a password hash and a role string. In production use a proper identity
/// system instead of this minimal representation.
/// </summary>
public class AppUser
{
    /// <summary>
    /// Primary key for the user.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// User-chosen username (unique constraint should be enforced elsewhere).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Email address for the user.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Salted and hashed password. Do not store plaintext passwords.
    /// </summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty; // store salted hash

    /// <summary>
    /// Role assigned to the user for authorization checks (e.g. Customer, Admin).
    /// </summary>
    [Required]
    public string Role { get; set; } = "Customer"; // e.g. Customer, Admin, DeliveryAdmin, ShippingAdmin
}
