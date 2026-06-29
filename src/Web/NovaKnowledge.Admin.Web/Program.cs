using NovaKnowledge.Admin.Web.Components;
using NovaKnowledge.Admin.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<AdminApiClient>(client =>
{
    var gatewayUrl = builder.Configuration["GatewayUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(gatewayUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
