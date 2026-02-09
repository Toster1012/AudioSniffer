using System.Text;
using System.Text.Json;

namespace AudioSniffer.Components.Middleware;

public class SecurityLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityLoggingMiddleware> _logger;

    public SecurityLoggingMiddleware(RequestDelegate next, ILogger<SecurityLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext http_context)
    {
        string client_ip_address = http_context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string http_method = http_context.Request.Method;
        string request_path = http_context.Request.Path;
        string query_string = http_context.Request.QueryString.ToString();
        string user_agent = http_context.Request.Headers.UserAgent.ToString();

        object request_information = new
        {
            Timestamp = DateTime.UtcNow,
            ClientIp = client_ip_address,
            Method = http_method,
            Path = request_path,
            QueryString = query_string,
            UserAgent = user_agent,
            IsHttps = http_context.Request.IsHttps,
            ContentType = http_context.Request.ContentType,
            ContentLength = http_context.Request.ContentLength
        };

        _logger.LogInformation("Security Log: {RequestInfo}", JsonSerializer.Serialize(request_information));

        try
        {
            await _next(http_context);
        }
        catch (Exception caught_exception)
        {
            _logger.LogError(caught_exception, "Security Error: {ErrorMessage}", caught_exception.Message);
            throw;
        }

        if (http_context.Response.StatusCode >= 400)
        {
            _logger.LogWarning("Security Warning: Status {StatusCode} for {Path} from {ClientIp}",
                http_context.Response.StatusCode, request_path, client_ip_address);
        }
    }
}