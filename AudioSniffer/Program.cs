using AudioSniffer.Services;
using AudioSniffer.Components.Middleware;
using AudioSniffer.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.DataProtection;
using System.Security;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.UseHttps();
    });
});

builder.WebHost.UseUrls("https://localhost:8000");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();

builder.Services.AddControllers();

builder.Services.AddScoped<IAudioAnalysisService, AudioAnalysisService>();

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddScoped<IRequestHistoryService, RequestHistoryService>();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddMemoryCache();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "X-CSRF-TOKEN-COOKIE";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.FormFieldName = "X-CSRF-TOKEN-FIELD";
});

builder.Services.AddDataProtection();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    using var ctx = await dbFactory.CreateDbContextAsync();
    await ctx.Database.EnsureCreatedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        if (exception is SecurityException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                code = "security_exception",
                message = "Access denied due to security restrictions"
            }));
        }
        else
        {
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                code = "server_error",
                message = "An unexpected error occurred"
            }));
        }
    });
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseSession();

app.UseMiddleware<RequestSizeLimitMiddleware>(
    app.Services.GetRequiredService<IConfiguration>(),
    app.Services.GetRequiredService<ILogger<RequestSizeLimitMiddleware>>());

app.UseMiddleware<RequestValidationMiddleware>(
    app.Services.GetRequiredService<IConfiguration>(),
    app.Services.GetRequiredService<ILogger<RequestValidationMiddleware>>());

app.UseMiddleware<SecurityLoggingMiddleware>();
app.UseMiddleware<XssProtectionMiddleware>(
    app.Services.GetRequiredService<ILogger<XssProtectionMiddleware>>());

app.UseMiddleware<RateLimitingMiddleware>(
    app.Services.GetRequiredService<IMemoryCache>(),
    app.Services.GetRequiredService<ILogger<RateLimitingMiddleware>>());

app.UseMiddleware<AdminAuthMiddleware>(
    app.Services.GetRequiredService<IDataProtectionProvider>(),
    app.Services.GetRequiredService<ILogger<AdminAuthMiddleware>>());

app.Use(async (context, next) =>
{
    if (context.Request.Path.HasValue)
    {
        var path = context.Request.Path.Value;
        if (path.Contains("'") || path.Contains("--") || path.Contains(";") ||
            path.Contains("/*") || path.Contains("*/") || path.Contains("xp_") ||
            path.Contains("exec ") || path.Contains("union ") || path.Contains("select ") ||
            path.Contains("insert ") || path.Contains("update ") || path.Contains("delete "))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "error",
                code = "suspicious_request",
                message = "Request contains suspicious patterns"
            }));
            return;
        }
    }

    string[] sensitiveCustomHeaders = ["x-custom", "x-api-key", "x-forwarded-for-custom"];

    foreach (var header in context.Request.Headers)
    {
        var key = header.Key.ToLowerInvariant();
        if (sensitiveCustomHeaders.Contains(key))
        {
            var value = header.Value.ToString();
            if (value.Contains("'") || value.Contains("--") || value.Contains(";") ||
                value.Contains("xp_") || value.Contains("exec ") || value.Contains("union "))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "error",
                    code = "suspicious_header",
                    message = "Request contains suspicious header values"
                }));
                return;
            }
        }
    }

    await next();
});

app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("X-XSS-Protection", "1; mode=block");
    headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'self';");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=(), payment=()");
    headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
    headers.Append("Feature-Policy", "accelerometer 'none'; ambient-light-sensor 'none'; autoplay 'none'; battery 'none'; camera 'none'; display-capture 'none'; document-domain 'none'; encrypted-media 'none'; execution-while-not-rendered 'none'; execution-while-out-of-viewport 'none'; fullscreen 'self'; geolocation 'none'; gyroscope 'none'; magnetometer 'none'; microphone 'none'; midi 'none'; navigation-override 'none'; picture-in-picture 'none'; publickey-credentials-get 'none'; screen-wake-lock 'none'; sync-xhr 'none'; usb 'none'; web-share 'none'; xr-spatial-tracking 'none';");

    await next();
});

app.UseAntiforgery();

app.MapStaticAssets();

app.MapControllers();

app.MapRazorComponents<AudioSniffer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();