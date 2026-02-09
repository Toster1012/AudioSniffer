using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace AudioSniffer.Components.Middleware
{
    public class RequestSizeLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly int _maxRequestSize;
        private readonly ILogger<RequestSizeLimitMiddleware> _logger;

        public RequestSizeLimitMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<RequestSizeLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            _maxRequestSize = configuration.GetValue<int>("Security:MaxRequestSizeBytes", 10485760); // 10MB по умолчанию
        }

        public async Task InvokeAsync(HttpContext http_context)
        {
            // Проверка размера запроса
            if (http_context.Request.ContentLength.HasValue && http_context.Request.ContentLength.Value > _maxRequestSize)
            {
                _logger.LogWarning("Request size limit exceeded from IP: {IpAddress}. Size: {RequestSize} bytes, Limit: {MaxSize} bytes",
                    http_context.Connection.RemoteIpAddress, http_context.Request.ContentLength.Value, _maxRequestSize);

                http_context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                http_context.Response.ContentType = "application/json";
                await http_context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "error",
                    code = "request_too_large",
                    message = $"Request size exceeds the maximum allowed size of {_maxRequestSize} bytes"
                }));
                return;
            }

            await _next(http_context);
        }
    }
}