using System.Net;
using System.Threading.RateLimiting;
using AudioSniffer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddScoped<IAudioAnalysisService, AudioAnalysisService>();
builder.Services.AddSingleton<IRequestHistoryService, RequestHistoryService>();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(context =>
    {
        var ip = context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHsts(opt =>
{
    opt.MaxAge = TimeSpan.FromDays(365);
    opt.IncludeSubDomains = true;
    opt.Preload = true;
});

builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(IPAddress.Loopback, 8000, lo =>
    {
        lo.UseHttps();
    });
});

var app = builder.Build();

app.UseHsts();
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "connect-src 'self' wss: ws:; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-XSS-Protection"] = "1; mode=block";
    headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Cross-Origin-Resource-Policy"] = "same-origin";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

app.UseRateLimiter();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<AudioSniffer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
