using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace AudioSniffer.Controllers
{
    [ApiController]
    [Route("admin")]
    public class AdminAuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _protector;
        private readonly IAntiforgery _antiforgery;
        private readonly ILogger<AdminAuthController> _logger;

        public AdminAuthController(IConfiguration configuration, IDataProtectionProvider dataProtectionProvider, IAntiforgery antiforgery, ILogger<AdminAuthController> logger)
        {
            _configuration = configuration;
            _protector = dataProtectionProvider.CreateProtector("AdminAuthMiddleware");
            _antiforgery = antiforgery;
            _logger = logger;
        }

        [HttpPost("auth")]
        public IActionResult Authenticate([FromBody] AdminAuthRequest auth_request)
        {
            if (string.IsNullOrEmpty(auth_request?.Key))
            {
                return BadRequest("Key is required");
            }

            string expected_admin_key = _configuration["Admin:AccessKey"] ?? "SECURE_ADMIN_KEY_123";

            _logger.LogInformation("Admin auth attempt - Received key: {ReceivedKey}, Expected key: {ExpectedKey}", auth_request.Key, expected_admin_key);
            _logger.LogInformation("Keys match: {KeysMatch}", auth_request.Key == expected_admin_key);

            if (auth_request.Key == expected_admin_key)
            {
                string protected_value = _protector.Protect(auth_request.Key);
                CookieOptions cookie_options = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(30),
                    IsEssential = true
                };

                Response.Cookies.Append("admin_access_secure", protected_value, cookie_options);

                _logger.LogInformation("Successful admin authentication from IP: {IpAddress}", HttpContext.Connection.RemoteIpAddress);

                return Ok(new { success = true });
            }

            _logger.LogWarning("Failed admin authentication attempt from IP: {IpAddress}", HttpContext.Connection.RemoteIpAddress);

            string attempt_tracking_key = $"admin_auth_attempt_{HttpContext.Connection.RemoteIpAddress}";
            int failed_attempts = HttpContext.Session?.GetInt32(attempt_tracking_key) ?? 0;
            failed_attempts++;
            HttpContext.Session?.SetInt32(attempt_tracking_key, failed_attempts);

            return Unauthorized(new { success = false, message = "Invalid key" });
        }
    }

    public class AdminAuthRequest
    {
        public string Key { get; set; } = string.Empty;
    }
}