using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FinanceTracker.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _db;

        public AuthController(IConfiguration config, ApplicationDbContext db)
        {
            _config = config;
            _db = db;
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] AppUser user)
        {
            if (_db.Users.Any(u => u.Username == user.Username))
                return BadRequest("User already exists.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            _db.Users.Add(user);
            _db.SaveChanges();

            return Ok("Registered");
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] AppUser login)
        {
            var user = _db.Users.SingleOrDefault(u => u.Username == login.Username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(login.PasswordHash, user.PasswordHash))
                return Unauthorized("Invalid username or password.");

            var token = GenerateToken(user);
            return Ok(new { token });
        }
        // POST: api/auth/logout
        // Server-side invalidation: stores the bearer token in RevokedTokens table.
        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return BadRequest("No bearer token found.");

            var token = authHeader.Substring("Bearer ".Length).Trim();

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return BadRequest("Invalid token.");

            var jwt = handler.ReadJwtToken(token);

            // Determine expiry from token (exp is seconds since epoch)
            DateTime expiresAt = DateTime.UtcNow;
            var expClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
            if (long.TryParse(expClaim, out var expSeconds))
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
            }
            else
            {
                // fallback: use a small TTL to avoid infinite blacklist entries
                expiresAt = DateTime.UtcNow.AddHours(1);
            }

            if (!_db.RevokedTokens.Any(rt => rt.Token == token))
            {
                _db.RevokedTokens.Add(new RevokedToken
                {
                    Token = token,
                    ExpiresAt = expiresAt
                });
                _db.SaveChanges();
            }

            return Ok("Logged out");
        }

        private string GenerateToken(AppUser user)
        {
            var jwtSettings = _config.GetSection("Jwt");

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("UserId", user.Id.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}
