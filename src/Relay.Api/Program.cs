using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Relay.Api;
using Relay.Data;
using Relay.Domain;

var builder = WebApplication.CreateBuilder(args);

// relay.db lives next to the API project unless configured otherwise. Delete it to re-seed.
var dbPath = builder.Configuration["Relay:DatabasePath"] ?? "relay.db";
if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(builder.Environment.ContentRootPath, dbPath);

builder.Services.AddDbContext<RelayDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<ActivityStore>();
builder.Services.AddSingleton(new AssessmentEngine(EngineOptions.Default));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Schema and seed are regular EF migrations (§3), so running the API is enough to get a ready database.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<RelayDbContext>().Database.Migrate();
}

app.MapRelayApi();
app.MapGet("/", () => Results.Redirect("/api/meta"));

app.Run();

public partial class Program;
