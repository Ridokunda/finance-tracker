using FinanceTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using FinanceTracker.Api.Services;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddScoped<MonthlyAnalytics>();
builder.Services.AddSingleton<StatementParser>();
builder.Services.AddHostedService<ImportCleanup>();
builder.Services.AddHttpClient<IClassifierClient, ClassifierClient>(client => {
    client.BaseAddress = new Uri(builder.Configuration["Classifier:BaseUrl"] ?? "http://localhost:8000/");
    client.Timeout = TimeSpan.FromSeconds(3);
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new InvalidOperationException("Jwt:Key is required."));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };

    // Reject tokens that have been revoked (server-side logout)
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            var token = authHeader?.Split(' ').Last();

            if (!string.IsNullOrEmpty(token))
            {
                var revoked = db.RevokedTokens.SingleOrDefault(rt => rt.Token == token);
                if (revoked != null)
                {
                    context.Fail("Token has been revoked.");
                }
            }

            return Task.CompletedTask;
        }
    };
});


var app = builder.Build();
if (args.Contains("--migrate") || args.Contains("--demo-seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    if (args.Contains("--demo-seed")) await DemoSeeder.Seed(db);
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();



if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
