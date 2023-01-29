using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using WebVite;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSpaStaticFiles(x => { x.RootPath = "ClientApp/dist"; });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

app.UseSpaStaticFiles();
app.UseSpa(spa =>
{
    spa.Options.SourcePath = "ClientApp";
    spa.Options.DevServerPort = 5173;

    if (app.Environment.IsDevelopment())
        spa.UseViteDevelopmentServer("dev");
});

app.Run();