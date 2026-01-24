using System.Collections.Concurrent;
using System.Net;

namespace AudioSniffer.Components.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateLimitInfo> _clientRateLimits = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _timeWindow;

    public RateLimitingMiddleware(RequestDelegate next, int maxRequests = 100, int minutes = 1)
    {
        _next = next;
        _maxRequests = maxRequests;
        _timeWindow = TimeSpan.FromMinutes(minutes);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string _clientIpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (string.IsNullOrEmpty(_clientIpAddress) || _clientIpAddress == "::1" || _clientIpAddress.StartsWith("127."))
        {
            await _next(context);
            return;
        }

        RateLimitInfo _rateLimitInformation = _clientRateLimits.GetOrAdd(_clientIpAddress, _ => new RateLimitInfo());
        bool _shouldLimitRequest = false;

        lock (_rateLimitInformation)
        {
            DateTime _currentTime = DateTime.UtcNow;
            _rateLimitInformation.Requests = _rateLimitInformation.Requests.Where(t => _currentTime - t < _timeWindow).ToList();
            _rateLimitInformation.Requests.Add(_currentTime);

            if (_rateLimitInformation.Requests.Count > _maxRequests)
            {
                _shouldLimitRequest = true;
            }
        }

        if (_shouldLimitRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Too many requests. Please try again later.");
            return;
        }

        await _next(context);
    }

    private class RateLimitInfo
    {
        public List<DateTime> Requests { get; set; } = new List<DateTime>();
    }
}
