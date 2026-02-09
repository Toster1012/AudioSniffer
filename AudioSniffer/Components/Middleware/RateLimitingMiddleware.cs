using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http;

namespace AudioSniffer.Components.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly int _maxRequests;
    private readonly int _maxRequestsForSensitivePaths;
    private readonly TimeSpan _timeWindow;
    private readonly TimeSpan _banTimeWindow;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<RateLimitingMiddleware> logger,
        int maxRequests = 100, int maxRequestsForSensitivePaths = 20, int minutes = 1, int banMinutes = 15)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _maxRequests = maxRequests;
        _maxRequestsForSensitivePaths = maxRequestsForSensitivePaths;
        _timeWindow = TimeSpan.FromMinutes(minutes);
        _banTimeWindow = TimeSpan.FromMinutes(banMinutes);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string requestPath = context.Request.Path.ToString().ToLower();

        bool isSensitivePath = requestPath.Contains("/admin") ||
                               requestPath.Contains("/api") ||
                               requestPath.Contains("/auth") ||
                               requestPath.Contains("/login");

        bool isLocalRequest = string.IsNullOrEmpty(clientIp) ||
                              clientIp == "::1" ||
                              clientIp.StartsWith("127.") ||
                              clientIp.StartsWith("localhost");

        if (isLocalRequest && !context.Request.IsHttps)
        {
            int localMaxRequests = isSensitivePath ? 50 : 200;
            if (IsRateLimited(clientIp, localMaxRequests, _timeWindow))
            {
                await HandleRateLimitExceeded(context, "Local development request limit exceeded");
                return;
            }
        }
        else
        {
            int currentMaxRequests = isSensitivePath ? _maxRequestsForSensitivePaths : _maxRequests;

            if (IsRateLimited(clientIp, currentMaxRequests, _timeWindow))
            {
                if (IsBanned(clientIp))
                {
                    await HandleBan(context);
                    return;
                }

                await HandleRateLimitExceeded(context, "Request limit exceeded");
                return;
            }
        }

        await _next(context);
    }

    private bool IsRateLimited(string clientIp, int maxRequests, TimeSpan timeWindow)
    {
        string cacheKey = $"ratelimit_{clientIp}";
        RateLimitInfo? rateLimitInfo = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.SetSlidingExpiration(timeWindow);
            return new RateLimitInfo();
        });

        if (rateLimitInfo != null)
        {
            lock (rateLimitInfo)
            {
                DateTime currentTime = DateTime.UtcNow;
                rateLimitInfo.Requests = rateLimitInfo.Requests.Where(t => currentTime - t < timeWindow).ToList();
                rateLimitInfo.Requests.Add(currentTime);

                if (rateLimitInfo.Requests.Count > maxRequests)
                {
                    _logger.LogWarning("Rate limit exceeded for IP: {IpAddress}. Requests: {RequestCount}", clientIp, rateLimitInfo.Requests.Count);
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsBanned(string clientIp)
    {
        string banCacheKey = $"ban_{clientIp}";
        return _cache.TryGetValue(banCacheKey, out _);
    }

    private void BanClient(string clientIp)
    {
        string banCacheKey = $"ban_{clientIp}";
        _cache.Set(banCacheKey, true, _banTimeWindow);
        _logger.LogWarning("Client banned for {BanMinutes} minutes: {IpAddress}", _banTimeWindow.TotalMinutes, clientIp);
    }

    private async Task HandleRateLimitExceeded(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        int requestCount = GetRequestCount(clientIp);

        if (requestCount > _maxRequests * 2)
        {
            BanClient(clientIp);
        }

        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "error",
            code = "too_many_requests",
            message = message,
            retryAfter = (int)_timeWindow.TotalSeconds
        }));
    }

    private async Task HandleBan(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.ContentType = "application/json";

        string banCacheKey = $"ban_{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        if (_cache.TryGetValue(banCacheKey, out DateTime banExpiry))
        {
            TimeSpan remainingTime = banExpiry - DateTime.UtcNow;
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                code = "client_banned",
                message = "Your IP has been temporarily banned due to excessive requests",
                bannedUntil = banExpiry,
                remainingTimeSeconds = (int)remainingTime.TotalSeconds
            }));
        }
        else
        {
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                code = "client_banned",
                message = "Your IP has been temporarily banned due to excessive requests"
            }));
        }
    }

    private int GetRequestCount(string clientIp)
    {
        string cacheKey = $"ratelimit_{clientIp}";
        if (_cache.TryGetValue(cacheKey, out RateLimitInfo? rateLimitInfo) && rateLimitInfo != null)
        {
            return rateLimitInfo.Requests.Count;
        }
        return 0;
    }

    private class RateLimitInfo
    {
        public List<DateTime> Requests { get; set; } = new List<DateTime>();
    }
}