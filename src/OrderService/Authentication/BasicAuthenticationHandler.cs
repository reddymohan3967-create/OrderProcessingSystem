using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderService.Data;

namespace OrderService.Authentication;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        AppDbContext db)
        : base(options, logger, encoder, clock)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.NoResult();

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
            if (!string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.NoResult();

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            if (credentials.Length != 2)
                return AuthenticateResult.Fail("Invalid Authorization header");

            var userOrEmail = credentials[0];
            var password = credentials[1];

            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == userOrEmail || u.Username == userOrEmail);
            if (user == null)
                return AuthenticateResult.Fail("Invalid username or password");

            bool ok = false;
            try
            {
                ok = OrderService.Utils.PasswordHasher.Verify(password, user.PasswordHash);
            }
            catch { ok = false; }

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
                            ok = (bool)verify.Invoke(null, new object[] { password, user.PasswordHash })!;
                        }
                    }
                }
                catch { }
            }

            if (!ok) ok = user.PasswordHash == password;

            if (!ok)
                return AuthenticateResult.Fail("Invalid username or password");

            var claims = new[] {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error authenticating");
            return AuthenticateResult.Fail("Error authenticating");
        }
    }
}
