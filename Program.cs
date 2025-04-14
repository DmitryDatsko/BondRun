using BondRun.Hubs;
using BondRun.Services;

var builder = WebApplication.CreateBuilder(args);

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddSignalR();
builder.Services.AddSingleton<BettingService>();
builder.Services.AddSingleton<CryptoPriceService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BettingService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<CryptoPriceService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policyBuilder =>
        {
            policyBuilder.WithOrigins("http://localhost:3000")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.UseCors(myAllowSpecificOrigins);
app.UseHttpsRedirection();

app.Run();
