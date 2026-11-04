using BuildingBlocks.Persistence;
using BuildingBlocks.ServiceDefaults;
using Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Logging, health checks and error format shared by every service.
builder.AddServiceDefaults();

// Catalog's own database. The connection string comes from configuration:
// appsettings.json for local runs, the ConnectionStrings__CatalogDb environment variable in Docker.
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("CatalogDb")!));

// Readiness depends on the database: if MySQL is down, this service can't serve requests.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-db", tags: [ServiceDefaultsExtensions.ReadyTag]);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();

// Swagger is on in every environment because this is a learning project.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapDefaultEndpoints();

await app.EnsureDatabaseAsync<CatalogDbContext>(CatalogSeed.SeedAsync);

app.Run();
