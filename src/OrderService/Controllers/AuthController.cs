using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Entities;
using System.Security.Claims;

namespace OrderService.Controllers;

[ApiController]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("PerIpPolicy")]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuthController(AppDbContext db) => _db = db;

    public class LoginRequest { public string UserOrEmail { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.UserOrEmail || u.Username == req.UserOrEmail);
        if (user == null) return Unauthorized();

        bool ok = false;
        try
        {
            ok = OrderService.Utils.PasswordHasher.Verify(req.Password, user.PasswordHash);
        }
        catch
        {
            ok = false;
        }

        if (!ok)
        {
            try
            {
                var bcryptType = Type.GetType("BCrypt.Net.BCrypt, BCrypt.Net-Next");
                if (bcryptType != null)
                {
                    var verify = bcryptType.GetMethod("Verify", new[] { typeof(string), typeof(string) });
                    if (verify != null)
                    {
                        ok = (bool)verify.Invoke(null, new object[] { req.Password, user.PasswordHash })!;
                    }
                }
            }
            catch { }
        }

        if (!ok)
            ok = user.PasswordHash == req.Password;

        if (!ok) return Unauthorized();

        // Return success immediately; client will not rely on cookies.
        return Ok(new { role = user.Role });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // No-op: basic auth is stateless
        return NoContent();
    }

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        if (!User.Identity?.IsAuthenticated ?? false) return Unauthorized();
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var email = User.Identity?.Name ?? string.Empty;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        return Ok(new { id, email, role });
    }
}
