using AudioSniffer.Services;
using AudioSniffer.Components.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.DataProtection;
using System.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddDbContext<AudioSniffer.Data.ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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


WebApplication app = builder.Build();

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

        Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature? exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        Exception? exception = exceptionHandlerPathFeature?.Error;

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

app.UseMiddleware<RequestSizeLimitMiddleware>(app.Services.GetRequiredService<IConfiguration>(), app.Services.GetRequiredService<ILogger<RequestSizeLimitMiddleware>>());

app.UseMiddleware<RequestValidationMiddleware>(app.Services.GetRequiredService<IConfiguration>(), app.Services.GetRequiredService<ILogger<RequestValidationMiddleware>>());

app.UseMiddleware<SecurityLoggingMiddleware>();
app.UseMiddleware<XssProtectionMiddleware>(app.Services.GetRequiredService<ILogger<XssProtectionMiddleware>>());
app.UseMiddleware<RateLimitingMiddleware>(app.Services.GetRequiredService<IMemoryCache>(), app.Services.GetRequiredService<ILogger<RateLimitingMiddleware>>());

app.UseMiddleware<AdminAuthMiddleware>(app.Services.GetRequiredService<IDataProtectionProvider>(), app.Services.GetRequiredService<ILogger<AdminAuthMiddleware>>());

app.Use((context, next) =>
{
    if (context.Request.Path.HasValue && (context.Request.Path.Value.Contains("'") ||
                                         context.Request.Path.Value.Contains("--") ||
                                         context.Request.Path.Value.Contains(";") ||
                                         context.Request.Path.Value.Contains("/*") ||
                                         context.Request.Path.Value.Contains("*/") ||
                                         context.Request.Path.Value.Contains("xp_") ||
                                         context.Request.Path.Value.Contains("exec ") ||
                                         context.Request.Path.Value.Contains("union ") ||
                                         context.Request.Path.Value.Contains("select ") ||
                                         context.Request.Path.Value.Contains("insert ") ||
                                         context.Request.Path.Value.Contains("update ") ||
                                         context.Request.Path.Value.Contains("delete ")))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "error",
            code = "suspicious_request",
            message = "Request contains suspicious patterns"
        }));
    }

    string[] sensitiveHeaders = new[] { "cookie", "authorization", "x-custom", "x-api-key", "x-requested-with" };
    foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in context.Request.Headers)
    {
        if (sensitiveHeaders.Contains(header.Key.ToLower()))
        {
            string headerValue = header.Value.ToString();
            if (headerValue.Contains("'") || headerValue.Contains("--") || headerValue.Contains(";"))
            {

            }
        }
    }

    return next();
});

app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use((context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'self';");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=(), payment=()");
    context.Response.Headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
    context.Response.Headers.Append("Feature-Policy", "accelerometer 'none'; ambient-light-sensor 'none'; autoplay 'none'; battery 'none'; camera 'none'; display-capture 'none'; document-domain 'none'; encrypted-media 'none'; execution-while-not-rendered 'none'; execution-while-out-of-viewport 'none'; fullscreen 'self'; geolocation 'none'; gyroscope 'none'; magnetometer 'none'; microphone 'none'; midi 'none'; navigation-override 'none'; picture-in-picture 'none'; publickey-credentials-get 'none'; screen-wake-lock 'none'; sync-xhr 'none'; usb 'none'; web-share 'none'; xr-spatial-tracking 'none';");

    return next();
});

app.UseAntiforgery();

app.MapStaticAssets();

app.MapControllers();

app.MapRazorComponents<AudioSniffer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
