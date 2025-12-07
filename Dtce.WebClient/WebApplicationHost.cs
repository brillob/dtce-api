using Dtce.Identity;
using Dtce.Identity.Stores;
using Dtce.WebClient.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<DtceApiService>();
builder.Services.AddSingleton<JobHistoryService>();

var storageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(storageConnectionString))
{
    builder.Services.AddSingleton<IUserStore>(_ => new AzureTableUserStore(storageConnectionString!));
}
else
{
    builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
}

builder.Services.AddSingleton<IUserService, UserService>();

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddSession();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
