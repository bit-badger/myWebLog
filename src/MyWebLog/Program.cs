using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyWebLog;
using MyWebLog.Features;
using MyWebLog.Features.Users;
using System.Reflection;

if (args.Length > 0 && args[0] == "init")
{
    await InitDb();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMvc(opts =>
{
    opts.Conventions.Add(new FeatureControllerModelConvention());
    opts.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
}).AddRazorOptions(opts =>
{
    opts.ViewLocationFormats.Clear();
    opts.ViewLocationFormats.Add("/Themes/{3}/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Themes/{3}/Shared/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Themes/Default/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Themes/Default/Shared/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Features/{2}/{1}/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Features/{2}/{0}.cshtml");
    opts.ViewLocationFormats.Add("/Features/Shared/{0}.cshtml");
    opts.ViewLocationExpanders.Add(new FeatureViewLocationExpander());
    opts.ViewLocationExpanders.Add(new ThemeViewLocationExpander());
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opts =>
    {
        opts.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        opts.SlidingExpiration = true;
        opts.AccessDeniedPath = "/forbidden";
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddDbContext<WebLogDbContext>(o =>
{
    // TODO: can get from DI?
    var db = WebLogCache.HostToDb(new HttpContextAccessor().HttpContext!);
     // "empty";
    o.UseSqlite($"Data Source=Db/{db}.db");
});

// Load themes
Array.ForEach(Directory.GetFiles(Directory.GetCurrentDirectory(), "MyWebLog.Themes.*.dll"),
    it => { Assembly.LoadFile(it); });

var app = builder.Build();

app.UseCookiePolicy(new CookiePolicyOptions { MinimumSameSitePolicy = SameSiteMode.Strict });
app.UseMiddleware<WebLogMiddleware>();
app.UseAuthentication();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseEndpoints(endpoints => endpoints.MapControllers());

app.Run();

/// <summary>
/// Initialize a new database
/// </summary>
async Task InitDb()
{
    if (args.Length != 5)
    {
        Console.WriteLine("Usage: MyWebLog init [url] [name] [admin-email] [admin-pw]");
        return;
    }

    using var db = new WebLogDbContext(new DbContextOptionsBuilder<WebLogDbContext>()
        .UseSqlite($"Data Source=Db/{args[1].Replace(':', '_')}.db").Options);
    await db.Database.MigrateAsync();
    
    // Create the admin user
    var salt = Guid.NewGuid();
    var user = new WebLogUser
    {
        Id = WebLogDbContext.NewId(),
        UserName = args[3],
        FirstName = "Admin",
        LastName = "User",
        PreferredName = "Admin",
        PasswordHash = UserController.HashedPassword(args[4], args[3], salt),
        Salt = salt,
        AuthorizationLevel = AuthorizationLevel.Administrator
    };
    await db.Users.AddAsync(user);

    // Create the default home page
    var home = new Page
    {
        Id = WebLogDbContext.NewId(),
        AuthorId = user.Id,
        Title = "Welcome to myWebLog!",
        Permalink = "welcome-to-myweblog.html",
        PublishedOn = DateTime.UtcNow,
        UpdatedOn = DateTime.UtcNow,
        Text = "<p>This is your default home page.</p>",
        Revisions = new[]
        {
            new PageRevision
            {
                Id = WebLogDbContext.NewId(),
                AsOf = DateTime.UtcNow,
                SourceType = RevisionSource.Html,
                Text = "<p>This is your default home page.</p>"
            }
        }
    };
    await db.Pages.AddAsync(home);

    // Add the details
    var timeZone = TimeZoneInfo.Local.Id;
    if (!TimeZoneInfo.Local.HasIanaId)
    {
        timeZone = TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone, out var ianaId)
            ? ianaId
            : throw new TimeZoneNotFoundException($"Cannot find IANA timezone for {timeZone}");
    }
    var details = new WebLogDetails
    {
        Name = args[2],
        UrlBase = args[1],
        DefaultPage = home.Id,
        TimeZone = timeZone
    };
    await db.WebLogDetails.AddAsync(details);

    await db.SaveChangesAsync();

    Console.WriteLine($"Successfully initialized database for {args[2]} with URL base {args[1]}");
}
