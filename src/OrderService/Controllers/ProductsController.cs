using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;

namespace OrderService.Controllers;

[ApiController]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("PerIpPolicy")]
[Route("api/[controller]")]
/// <summary>
/// API controller exposing product catalog endpoints.
/// </summary>
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    /// <summary>
    /// Retrieves all products with basic metadata (id, name, price, icon).
    /// </summary>
    public async Task<IActionResult> GetAll()
    {
        var products = await _db.Products
            .OrderBy(p => p.Id)
            .Select(p => new { id = p.Id, name = p.Name, price = p.Price, icon = p.Icon })
            .ToListAsync();

        return Ok(products);
    }
}
