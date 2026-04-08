using ExcelRenderer.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton<ExcelRenderService>();
builder.Services.AddSingleton<ContractNormalizationService>();
builder.Services.AddHttpClient<GraphSharePointUploadService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(15);
});

builder.Build().Run();
