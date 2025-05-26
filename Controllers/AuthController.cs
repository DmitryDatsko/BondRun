using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BondRun.Configuration;
using BondRun.Models;
using BondRun.Services.Token;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nethereum.Signer;

namespace BondRun.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IOptions<EnvVariables> envVariables, IUserIdentity userIdentity) : ControllerBase
{
    private readonly EnvVariables _envVariables = envVariables.Value;

    [HttpPost("verify")]
    public IActionResult Authenticate([FromBody] AuthenticationRequest request)
    {
        var message = request.Message;
        var signature = request.Signature;

        var signer = new EthereumMessageSigner();
        
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(message, signature);

        if (!string.IsNullOrEmpty(recoveredAddress))
        {
            var accessToken = CreateToken(new User { Id = Guid.NewGuid(), Address = recoveredAddress });
            
            if (!string.IsNullOrEmpty(accessToken))
            {
                SetCookie(accessToken, HttpContext);
                return Ok(new { message = "Authentication successful" });
            }
        }

        return Unauthorized(new { message = "Invalid signature" });
    }

    [HttpGet("nonce")]
    public IActionResult Nonce()
    {
        return Ok(GenerateSecureNonce());
    }
    
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var address = userIdentity.GetAddressByCookie();
        
        return string.IsNullOrEmpty(address) 
            ? Unauthorized() 
            : Ok(new { address });
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        RemoveCookie(HttpContext);

        return Unauthorized();
    }
    private string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new (ClaimTypes.NameIdentifier, user.Id.ToString()),
            new ("wallet_address", user.Address)
        };
        
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_envVariables.JwtTokenSecret));
        
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: credentials
        );
        
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        
        return jwt;
    }
    
    private void SetCookie(string accessToken, HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append(_envVariables.CookieName, accessToken,
            new CookieOptions
            {
                Path = "/",
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(1)
            });
    }
    private void RemoveCookie(HttpContext httpContext)
    {
        var deleteOptions = new CookieOptions
        {
            Path = "/",
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UnixEpoch
        };

        httpContext.Response.Cookies.Delete(_envVariables.CookieName, deleteOptions);
    }
    private static string GenerateSecureNonce(int length = 64)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var nonce = new char[length];
        using var rng = RandomNumberGenerator.Create();
        var buffer = new byte[sizeof(uint)];

        for (int i = 0; i < length; i++)
        {
            rng.GetBytes(buffer);
            uint num = BitConverter.ToUInt32(buffer, 0);
            nonce[i] = chars[(int)(num % (uint)chars.Length)];
        }

        return new string(nonce);
    }
}