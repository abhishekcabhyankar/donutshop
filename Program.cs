using DonutShop.Models;
using DonutShop.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Allow the antiforgery token to be supplied via a request header so the Apple Pay
// merchant-validation fetch() (which posts JSON, not a form) can be protected too.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Honor X-Forwarded-* headers from a reverse proxy (e.g. the nginx fronting
// Elastic Beanstalk / a load balancer) so the app sees the original HTTPS scheme.
// Without this, secure cookies are dropped and HTTPS redirection can loop.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // CloudFront terminates TLS at the edge and reaches the EB origin over HTTP,
    // so the original viewer protocol arrives in CloudFront-Forwarded-Proto, not
    // X-Forwarded-Proto (which nginx sets to "http" for the CloudFront->EB hop).
    options.ForwardedProtoHeaderName = "CloudFront-Forwarded-Proto";
    // Proxies are inside AWS; their addresses aren't known ahead of time.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Authorize.NET configuration (bind from appsettings + user-secrets/env).
builder.Services.Configure<AuthorizeNetOptions>(
    builder.Configuration.GetSection(AuthorizeNetOptions.SectionName));

// Apple Pay configuration (bind from appsettings + user-secrets/env).
builder.Services.Configure<ApplePayOptions>(
    builder.Configuration.GetSection(ApplePayOptions.SectionName));

// Session-backed shopping cart.
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// App services.
builder.Services.AddSingleton<IDonutCatalog, DonutCatalog>();
builder.Services.AddScoped<CartService>();
builder.Services.AddHttpClient<IPaymentService, AuthorizeNetPaymentService>();

var app = builder.Build();

// Apply forwarded headers before anything that depends on the request scheme.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve the Apple Pay domain-association file from wwwroot/.well-known. That file has
// no extension, so the default static-file middleware would 404 it; this scoped
// provider serves only /.well-known/* as text/plain.
var wellKnownPath = Path.Combine(app.Environment.WebRootPath, ".well-known");
if (Directory.Exists(wellKnownPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wellKnownPath),
        RequestPath = "/.well-known",
        ServeUnknownFileTypes = true,
        DefaultContentType = "text/plain"
    });
}

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
