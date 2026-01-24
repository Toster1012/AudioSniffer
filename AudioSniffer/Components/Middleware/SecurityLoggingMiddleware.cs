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

    public async Task InvokeAsync(HttpContext context)
    {
        string _clientIpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string _httpMethod = context.Request.Method;
        string _requestPath = context.Request.Path;
        string _queryString = context.Request.QueryString.ToString();
        string _userAgent = context.Request.Headers.UserAgent.ToString();

        var _requestInformation = new
        {
            Timestamp = DateTime.UtcNow,
            ClientIp = _clientIpAddress,
            Method = _httpMethod,
            Path = _requestPath,
            QueryString = _queryString,
            UserAgent = _userAgent,
            IsHttps = context.Request.IsHttps,
            ContentType = context.Request.ContentType,
            ContentLength = context.Request.ContentLength
        };

        _logger.LogInformation("Security Log: {RequestInfo}", JsonSerializer.Serialize(_requestInformation));

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Security Error: {ErrorMessage}", exception.Message);
            throw;
        }

        if (context.Response.StatusCode >= 400)
        {
            _logger.LogWarning("Security Warning: Status {StatusCode} for {Path} from {ClientIp}",
                context.Response.StatusCode, _requestPath, _clientIpAddress);
        }
    }
}
