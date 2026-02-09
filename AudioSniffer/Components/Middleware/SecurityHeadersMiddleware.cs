using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace AudioSniffer.Components.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext http_context)
        {
            // Устанавливаем дополнительные заголовки безопасности
            http_context.Response.Headers.Append("X-Permitted-Cross-Domain-Policies", "none");
            http_context.Response.Headers.Append("X-Download-Options", "noopen");
            http_context.Response.Headers.Append("X-DNS-Prefetch-Control", "off");
            http_context.Response.Headers.Append("Server", "SecureServer"); // Маскируем реальный сервер

            // Дополнительная защита от clickjacking
            http_context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");

            // Более строгая CSP для чувствительных страниц
            if (http_context.Request.Path.StartsWithSegments("/admin"))
            {
                http_context.Response.Headers.Remove("Content-Security-Policy");
                http_context.Response.Headers.Append("Content-Security-Policy",
                    "default-src 'self'; " +
                    "script-src 'self' 'nonce-{nonce}'; " + // Требуем nonce для скриптов
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data:; " +
                    "font-src 'self'; " +
                    "connect-src 'self'; " +
                    "frame-src 'none'; " +
                    "object-src 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self'; " +
                    "frame-ancestors 'none'; " +
                    "block-all-mixed-content; " +
                    "upgrade-insecure-requests");
            }

            await _next(http_context);
        }
    }
}