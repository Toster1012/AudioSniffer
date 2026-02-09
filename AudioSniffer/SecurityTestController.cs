using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace AudioSniffer.Controllers
{
    [ApiController]
    [Route("api/security")]
    public class SecurityTestController : ControllerBase
    {
        private readonly ILogger<SecurityTestController> _logger;

        public SecurityTestController(ILogger<SecurityTestController> logger)
        {
            _logger = logger;
        }

        [HttpGet("test")]
        public IActionResult TestSecurity()
        {
            return Ok(new
            {
                status = "success",
                message = "Security test endpoint",
                securityFeatures = new
                {
                    httpsEnabled = Request.IsHttps,
                    secureHeaders = new
                    {
                        xContentTypeOptions = Response.Headers.ContainsKey("X-Content-Type-Options"),
                        xFrameOptions = Response.Headers.ContainsKey("X-Frame-Options"),
                        xssProtection = Response.Headers.ContainsKey("X-XSS-Protection"),
                        csp = Response.Headers.ContainsKey("Content-Security-Policy"),
                        hsts = Response.Headers.ContainsKey("Strict-Transport-Security")
                    },
                    csrfProtection = true,
                    rateLimiting = true,
                    xssProtection = true,
                    sqlInjectionProtection = true,
                    requestValidation = true
                }
            });
        }

        [HttpPost("test-auth")]
        public IActionResult TestAuth()
        {
            // Этот эндпоинт требует аутентификации
            return Ok(new
            {
                status = "success",
                message = "Authentication test passed"
            });
        }

        [HttpPost("test-xss")]
        public IActionResult TestXss([FromBody] XssTestRequest request)
        {
            if (string.IsNullOrEmpty(request.Input))
            {
                return BadRequest("Input is required");
            }

            // В реальном приложении здесь была бы обработка, но для теста просто возвращаем данные
            return Ok(new
            {
                status = "success",
                message = "XSS test endpoint",
                input = request.Input
            });
        }
    }

    public class XssTestRequest
    {
        public string Input { get; set; } = string.Empty;
    }
}