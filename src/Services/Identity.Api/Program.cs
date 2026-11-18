using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.ServiceDefaults;
using Identity.Api.Data;
using Identity.Api.Domain;
using Identity.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validates incoming tokens (needed for GET /api/auth/me). Same settings as every other service.
builder.AddJwtAuthentication();

// Identity's own database: the only place user accounts and password hashes exist.
builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("IdentityDb")!));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<UsersDbContext>("identity-db", tags: [ServiceDefaultsExtensions.ReadyTag]);

// ASP.NET Core's password hasher (PBKDF2 with a random salt per password), without the rest of ASP.NET Core Identity.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication(); // reads the Bearer token into HttpContext.User
app.UseAuthorization();  // enforces [Authorize]

app.MapControllers();
app.MapDefaultEndpoints();

var hasher = app.Services.GetRequiredService<IPasswordHasher<User>>();
await app.EnsureDatabaseAsync<UsersDbContext>(db => UserSeed.SeedAsync(db, hasher));

app.Run();
