using Hangfire;
using Hangfire.PostgreSql; // PostgreSQL storage
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using SantaFeWaterSystem.Controllers;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Services;
using SantaFeWaterSystem.Settings;
using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using SantaFeWaterSystem.Hubs;
using DbEmailSettings = SantaFeWaterSystem.Models.EmailSettings;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Load appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// ------------------- PostgreSQL connection -------------------
// Use Railway DATABASE_URL if available
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

// EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Hangfire
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(connectionString));
builder.Services.AddHangfireServer();

// ------------------- Services -------------------
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<PdfService>();
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<SemaphoreSettings>(builder.Configuration.GetSection("SemaphoreSettings"));

// SMS services
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISemaphoreSmsService, MockSmsService>();
}
else
{
    builder.Services.AddHttpClient<ISemaphoreSmsService, SemaphoreSmsService>();
}

// Queue for notifications
builder.Services.AddSingleton<ISmsQueue, InMemorySmsQueue>();
builder.Services.AddHostedService<InMemorySmsQueue>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<LockoutService>();
builder.Services.AddScoped<PasswordPolicyService>();
builder.Services.AddScoped<BillingService>();

// ------------------- Logging -------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
Console.WriteLine("Environment: " + builder.Environment.EnvironmentName);

// ------------------- Cookie / Session / Auth -------------------
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSignalR();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/UserLogin";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// MVC
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ------------------- Seed default admin & email -------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var cultureInfo = new CultureInfo("en-PH");
    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

    if (!db.Users.Any(u => u.Username == "super_admin"))
    {
        var adminUser = new User
        {
            Username = "super_admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("st_Admin@6047"),
            Role = "Admin",
            IsMfaEnabled = false
        };
        db.Users.Add(adminUser);
        db.SaveChanges();
    }

    if (!db.EmailSettings.Any())
    {
        db.EmailSettings.Add(new DbEmailSettings
        {
            SmtpServer = "smtp.gmail.com",
            SmtpPort = 587,
            SenderName = "Santa Fe Water System",
            SenderEmail = "your@email.com",
            SenderPassword = "app-password"
        });
        db.SaveChanges();
    }
}

// ------------------- Middleware -------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseCookiePolicy();

// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire");

// Recurring daily backup
RecurringJob.AddOrUpdate<BackupController>(
    "DailyBackup",
    controller => controller.ScheduledBackup(),
    Cron.Daily(0, 0));

// Authentication / Session / Authorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// SignalR Hub
app.MapHub<VisitorHub>("/visitorHub");

// ------------------- Health check for Railway -------------------
app.MapGet("/", () => "OK");

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ------------------- Start server on Railway dynamic port -------------------
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://*:{port}");

app.Run();
