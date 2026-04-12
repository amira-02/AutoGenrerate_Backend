using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using AutoGenerate.Shared.Models;
using AutoGenerate.Shared.Data;

namespace AutoGenerate.Auth
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly JwtService _jwt;
        private readonly OtpService _otp;
        private readonly EmailService _email;

        public AuthController(AppDbContext context,
                              JwtService jwt,
                              OtpService otp,
                              EmailService email)
        {
            _context = context;
            _jwt = jwt;
            _otp = otp;
            _email = email;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto request)
        {
            if (_context.Users.Any(u => u.Email == request.Email))
                return BadRequest("Email already registered");

            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                Email = request.Email,
                PasswordHash = hash
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var code = _otp.GenerateOtp(user.Email);
            _email.SendOtp(user.Email, code);

            return Ok(new { message = "OTP sent" });
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto request)
        {
            if (!_otp.VerifyOtp(request.Email, request.Code))
                return BadRequest("Invalid OTP");

            var user = _context.Users.First(u => u.Email == request.Email);
            user.IsVerified = true;
            await _context.SaveChangesAsync();

            var token = _jwt.GenerateToken(user);

            return Ok(new { token });
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginDto request)
        {
            var user = _context.Users.FirstOrDefault(u => u.Email == request.Email);

            if (user == null)
                return Unauthorized();

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized();

            var token = _jwt.GenerateToken(user);

            return Ok(new { token });
        }
    }
}
