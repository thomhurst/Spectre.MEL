using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.MEL;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSpectreConsole();

var app = builder.Build();

app.MapGet("/", (ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Spectre.MEL.WebApi");
    logger.LogInformation("Greeting {User} at {Time}", "World", DateTimeOffset.Now);
    return Results.Ok(new { greeting = "Hello, world!" });
});

app.MapGet("/orders/{id:int}", (int id, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Spectre.MEL.WebApi.Orders");
    using (logger.BeginScope("Order {OrderId}", id))
    {
        logger.LogInformation("Loading order {OrderId}", id);
        if (id == 0)
        {
            logger.LogWarning("Order {OrderId} not found", id);
            return Results.NotFound();
        }
        return Results.Ok(new { id, total = 99.99m });
    }
});

app.MapPost("/login", (LoginRequest request, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Spectre.MEL.WebApi.Auth");
    logger.LogInformation("Login attempt for {Email} with password {Password}", request.Email, request.Password);
    return Results.Ok(new { token = "fake.jwt.token" });
});

app.Run();

internal sealed record LoginRequest(string Email, string Password);
