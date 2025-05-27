using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BondRun.Configuration;
using Microsoft.Extensions.Options;

namespace BondRun.Services.Token;

public class UserIdentity(IHttpContextAccessor accessor, IOptions<EnvVariables> envVariables) : IUserIdentity
{
    private readonly EnvVariables _envVariables = envVariables.Value;
    private bool HasWalletCookie()
    {
        var cookies = accessor.HttpContext?.Request.Cookies;
        
        return cookies != null && cookies.ContainsKey(_envVariables.CookieName);
    }
    public string GetAddressByCookie()
    {
        if(!HasWalletCookie()) return string.Empty;
        
        var cookies = accessor.HttpContext?.Request.Cookies;
        if (cookies == null || !cookies.TryGetValue(_envVariables.CookieName, out var token))
            return string.Empty;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}