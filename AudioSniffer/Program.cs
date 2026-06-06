using AudioSniffer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddScoped<IAudioAnalysisService, AudioAnalysisService>();
builder.Services.AddSingleton<IRequestHistoryService, RequestHistoryService>();

builder.WebHost.UseUrls("http://localhost:8000");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<AudioSniffer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
