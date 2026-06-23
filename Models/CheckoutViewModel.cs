using System.ComponentModel.DataAnnotations;

namespace DonutShop.Models;

public class CheckoutViewModel
{
    public Cart Cart { get; set; } = new();

    [Required]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Address")]
    public string Address { get; set; } = string.Empty;

    [Required]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [Display(Name = "State")]
    public string State { get; set; } = string.Empty;

    [Required]
    [Display(Name = "ZIP code")]
    public string Zip { get; set; } = string.Empty;

    // Populated client-side by Accept.js (no raw card data ever reaches the server)
    public string OpaqueDataDescriptor { get; set; } = string.Empty;
    public string OpaqueDataValue { get; set; } = string.Empty;
}

public class CheckoutResultViewModel
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public decimal AmountCharged { get; set; }
    public string? Message { get; set; }
}
