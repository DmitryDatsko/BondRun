using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BondRun.Services.Token;

public class UserIdentity : IUserIdentity
{
    public Guid GetIdByCookie(HttpRequest request)
    {
        try
        {
            string? accessToken = request.Cookies["XMN3bf8G9Vw3hSU"];

            if (string.IsNullOrEmpty(accessToken))
                return Guid.Empty;

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

            var claim = jwt.Claims.FirstOrDefault(i => i.Type == ClaimTypes.NameIdentifier);

            if (claim == null)
                return Guid.Empty;

            return Guid.TryParse(claim.Value, out Guid id) ? id : Guid.Empty;
        }
        catch
        {
            return Guid.Empty;
        }
    }
    
    public string GetAddressByCookie(HttpRequest request)
    {
        try
        {
            string? accessToken = request.Cookies["XMN3bf8G9Vw3hSU"];

            if (string.IsNullOrEmpty(accessToken))
                return string.Empty;

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

            var claim = jwt.Claims.FirstOrDefault(i => i.Type == "wallet_address");

            return claim == null ? string.Empty : claim.Value;
        }
        catch
        {
            return string.Empty;
        }
    }
}