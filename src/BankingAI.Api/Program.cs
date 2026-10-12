using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// One DbContext per HTTP request (scoped); never shared between requests. The connection string is
// read from configuration (the existing environment variable) when the context is created, so test
// hosts can override it. Migrations are never applied here: schema changes only run through the CLI.
builder.Services.AddDbContext<BankingDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>()[ConnectionStringKey]));

var app = builder.Build();

// Fail at startup with a clear message instead of on the first request.
if (string.IsNullOrWhiteSpace(app.Configuration[ConnectionStringKey]))
    throw new InvalidOperationException($"Set {ConnectionStringKey} before starting the API.");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// No HTTPS redirection yet: local development runs on HTTP only; TLS is decided with Docker (OPS-04).
app.MapControllers();

app.Run();

/// <summary>
/// Exposes the generated entry point to the integration tests (WebApplicationFactory&lt;Program&gt;).
/// </summary>
public partial class Program;
