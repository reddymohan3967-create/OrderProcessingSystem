using System.ComponentModel.DataAnnotations;

namespace OrderService.Entities;

public class AppUser
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty; // store salted hash

    [Required]
    public string Role { get; set; } = "Customer"; // e.g. Customer, Admin, DeliveryAdmin, ShippingAdmin
}
