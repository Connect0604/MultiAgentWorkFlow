using MultiAgentOrchestration.Blazor.Components;
using MultiAgentOrchestration.Blazor.Models;
using MultiAgentOrchestration.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<AgentFrameworkOptions>(builder.Configuration.GetSection(AgentFrameworkOptions.SectionName));
builder.Services.Configure<AgentRepositoryOptions>(builder.Configuration.GetSection(AgentRepositoryOptions.SectionName));
builder.Services.Configure<OrchestratorRepositoryOptions>(builder.Configuration.GetSection(OrchestratorRepositoryOptions.SectionName));
builder.Services.Configure<DefaultAgentsOptions>(builder.Configuration.GetSection(DefaultAgentsOptions.SectionName));
builder.Services.AddScoped<MultiAgentWorkflowService>();
builder.Services.AddScoped<IntentRoutingService>();
builder.Services.AddScoped<IAgentDefinitionRepository, AdoNetAgentDefinitionRepository>();
builder.Services.AddScoped<IOrchestratorRepository, AdoNetOrchestratorRepository>();
builder.Services.AddSingleton<AppSettingsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
