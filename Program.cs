using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BondRun.Configuration;
using BondRun.Data;
using BondRun.Filters;
using BondRun.Hubs;
using BondRun.Services;
using BondRun.Services.Hub;
using BondRun.Services.Monad;
using BondRun.Services.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

DotNetEnv.Env.Load();
var builder = WebApplication.CreateBuilder(args);

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddDbContextFactory<ApiDbContext>(opts =>
    opts.UseNpgsql(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")));
builder.Services.AddSignalR()
    .AddHubOptions<GameHub>(opts => opts.AddFilter<HubAuthorize>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BettingService>();
builder.Services.AddSingleton<HubAuthorize>();
builder.Services.AddSingleton<CryptoPriceService>();
builder.Services.AddSingleton<IUserIdentity, UserIdentity>();
builder.Services.AddSingleton<IMonadService, MonadService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<CryptoPriceService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<BettingService>());
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
    options.NetworkId = Environment.GetEnvironmentVariable("NETWORK_ID") ?? string.Empty;
    options.ContractAddress = Environment.GetEnvironmentVariable("CONTRACT_ADDRESS") ?? string.Empty;
    options.CookieName = Environment.GetEnvironmentVariable("WALLET_COOKIE_NAME") ?? string.Empty;
    options.PostgresConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? string.Empty;
});

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
        NameClaimType = JwtRegisteredClaimNames.Sub,
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
            if (context.Request.Cookies.TryGetValue(Environment.GetEnvironmentVariable("WALLET_COOKIE_NAME")!, out var token))
            {
                context.Token = token;
            }

            if (string.IsNullOrEmpty(context.Token))
            {
                context.Token = context.Request.Query[Environment.GetEnvironmentVariable("WALLET_COOKIE_NAME")!];
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
