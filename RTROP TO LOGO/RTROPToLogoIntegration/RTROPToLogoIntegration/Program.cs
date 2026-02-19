using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient; // Ensure this is present
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RTROPToLogoIntegration.Infrastructure.Identity; // Updated Namespace
using RTROPToLogoIntegration.Infrastructure.Persistence;
using RTROPToLogoIntegration.Middlewares;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using RTROPToLogoIntegration.Infrastructure.Extensions;
using RTROPToLogoIntegration.Infrastructure.Extensions;
using RTROPToLogoIntegration.Infrastructure.Services;
using Microsoft.OpenApi.Models;
using Wolverine;
using Microsoft.Data.SqlClient; // SQL Client ekle
using RTROPToLogoIntegration.Infrastructure.Middleware; // Middleware namespace is required

var builder = WebApplication.CreateBuilder(args);

// MANUEL DB KONTROLÜ (Serilog başlamadan önce)
// Serilog, SQL'e yazmaya çalışmadan önce veritabanının var olduğundan emin olmalıyız.
try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .Build();

    var connectionString = configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine("Veritabanı kontrolü yapılıyor...");
    EnsureDatabaseExists(connectionString);
    Console.WriteLine("Veritabanı kontrolü tamamlandı.");
}
catch (Exception ex)
{
    Console.WriteLine($"Kritik Hata: Veritabanı oluşturulamadı. Detay: {ex.Message}");
}

// Serilog Yapılandırması
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// ... Helper Method ...


try
{
    Log.Information("Uygulama başlatılıyor...");

    // Add services to the container.
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Logo MRP API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement()
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    },
                    Scheme = "oauth2",
                    Name = "Bearer",
                    In = ParameterLocation.Header,
                },
                new List<string>()
            }
        });
    });

    // Veritabanı Bağlantıları
    builder.Services.AddDbContext<AppIdentityDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Identity Servisleri
    builder.Services.AddIdentity<IdentityUser, IdentityRole>()
        .AddEntityFrameworkStores<AppIdentityDbContext>()
        .AddDefaultTokenProviders();

    // JWT Authentication
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]))
        };
    });

    // Custom Servisler
    // Interface kaydı
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<TokenService>(); // Controller direkt class istiyorsa (ki genelde interface istenir ama önceki kod class idi)
    builder.Services.AddScoped<StockRepository>();
    builder.Services.AddScoped<LogoClientService>();
    builder.Services.AddScoped<ExcelService>();
    builder.Services.AddScoped<AuditRepository>(); // Audit Repository Kaydı
    builder.Services.AddScoped<MrpItemParameterRepository>();

    // Wolverine Yapılandırması
    builder.Host.UseWolverine(opts =>
    {
        // Handler'ları otomatik bulur (Convention-based)
    });

    // EPPlus License Context (Opsiyonel)
    // ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

    // Forwarded Headers Yapılandırması (Middleware'den önce gelmeli)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    });

    var app = builder.Build();

    // Veritabanı Migration ve Seed
    // Not: IHost extension metodunu kullanıyoruz.
    // app.MigrateDatabase(); // Extension namespace sorununu çözmek gerekebilir veya direkt burada çağırılabilir.
    // Basitlik için burada scope oluşturup çağırabiliriz veya Extension metodunu statik sınıftan çağırabiliriz.
    // Ancak MigrationManager static class ve extension metod olarak tanımlandı.
    MigrationManager.MigrateDatabase(app);


    // Forwarded Headers Middleware (En başta olmalı)
    app.UseForwardedHeaders();

    // Custom IP Logging Middleware (Serilog Context için)
    app.UseMiddleware<IpLoggingMiddleware>();

    // Serilog Request Logging (İsteğe bağlı, HTTP isteklerini loglar)
    app.UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    Console.WriteLine($"Current Environment: {app.Environment.EnvironmentName}");
    
    if (app.Environment.IsDevelopment()) 
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    // Audit Middleware (Auth'dan önce çalışmalı ki 401 alan istekleri de loglayabilsin, veya auth sonrası sadece geçerli istekleri loglayabilir. 
    // İsteğin "Kanıt Niteliği" için genelde en dışa yakın olması iyidir ama Body okuma maliyeti yüzünden Auth arkasına da konabilir.
    // Kullanıcı talebi: "Middleware'i app.UseAuthentication() satırından ÖNCE ekle."
    app.UseMiddleware<RequestAuditMiddleware>();

    app.UseAuthentication();
    app.UseAuthorization();
    
    // Tenant Middleware (Controller'lardan önce, Auth'dan sonra olabilir veya önce)
    // Genelde Auth -> Tenant -> Controller sırası mantıklıdır.
    app.UseMiddleware<TenantMiddleware>();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama beklenmeyen bir hata ile sonlandı.");
}
finally
{
    Log.CloseAndFlush();
}

static void EnsureDatabaseExists(string connectionString)
{
    var builder = new SqlConnectionStringBuilder(connectionString);
    var originalDatabase = builder.InitialCatalog;
    
    // Master veritabanına bağlan
    builder.InitialCatalog = "master";
    
    using var connection = new SqlConnection(builder.ConnectionString);
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{originalDatabase}') CREATE DATABASE [{originalDatabase}]";
    command.ExecuteNonQuery();
}
