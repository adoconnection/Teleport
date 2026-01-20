using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("TELEPORT_");

var storePath = builder.Configuration["StorePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Store");
if (!Directory.Exists(storePath))
{
    Directory.CreateDirectory(storePath);
}

var accessKey = builder.Configuration["AccessKey"];
var accessKeyFile = Path.Combine(storePath, ".accesskey");

if (string.IsNullOrWhiteSpace(accessKey))
{
    if (File.Exists(accessKeyFile))
    {
        accessKey = File.ReadAllText(accessKeyFile).Trim();
    }
    else
    {
        accessKey = GenerateAccessKey();
        File.WriteAllText(accessKeyFile, accessKey);
        Console.WriteLine($"Generated new access key, saved to {accessKeyFile}");
    }
}

builder.Configuration["AccessKey"] = accessKey;
builder.Configuration["StorePath"] = storePath;

builder.Services.AddControllers();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});

// Allow synchronous I/O globally (required for SharpCompress)
//builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
//{
//    options.AllowSynchronousIO = true;
//});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
    options.AllowSynchronousIO = true;
});

var app = builder.Build();

var urls = app.Urls.Count > 0 ? app.Urls : new[] { "http://localhost:5000" };
var endpoint = builder.Configuration["ASPNETCORE_URLS"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? urls.First();

app.UseStaticFiles();

Console.WriteLine();
Console.WriteLine("===========================================");
Console.WriteLine("Teleport Server v6");
Console.WriteLine($"Store path: {storePath}");
Console.WriteLine();
Console.WriteLine("Client config (copy this line):");
Console.WriteLine($"{endpoint}|{accessKey}");
Console.WriteLine("===========================================");
Console.WriteLine();

app.MapControllers();
app.MapGet("/", () => "Teleport v6");

app.Run();

static string GenerateAccessKey()
{
    var bytes = new byte[24];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
}
