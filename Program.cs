using System.Text;
using BondRun.Configuration;
using BondRun.Hubs;
using BondRun.Services;
using BondRun.Services.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.IdentityModel.Tokens;

DotNetEnv.Env.Load();
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

builder.Services.Configure<EnvVariables>(options =>
{
    options.RpcUrl = Environment.GetEnvironmentVariable("RPC_URL") ?? string.Empty;
    options.PrivateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ?? string.Empty;
    options.JwtTokenSecret = Environment.GetEnvironmentVariable("JWT_TOKEN_SECRET") ?? string.Empty;
});

builder.Services.AddScoped<IUserIdentity, UserIdentity>();
builder.Services.AddHostedService<BettingService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    //options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new()
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.ASCII.GetBytes(
                Environment.GetEnvironmentVariable("JWT_TOKEN_SECRET")!
            )
        )
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue("XMN3bf8G9Vw3hSU", out var token))
            {
                context.Token = token;
            }

            if (string.IsNullOrEmpty(context.Token))
            {
                context.Token = context.Request.Query["XMN3bf8G9Vw3hSU"];
            }
            
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(myAllowSpecificOrigins);

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    HttpOnly = HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.UseHttpsRedirection();

app.Run();
