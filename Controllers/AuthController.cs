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
[Route("api/[controller]")]
public class AuthController(IOptions<JwtConfig> jwtConfig, IUserIdentity userIdentity) : ControllerBase
{
    private readonly JwtConfig _jwtConfig = jwtConfig.Value;
    private readonly IUserIdentity _userIdentity = userIdentity;
    
    [HttpPost("verify")]
    public IActionResult Authenticate([FromBody] AuthenticationRequest request)
    {
        var message = request.Message;
        var signature = request.Signature;

        var signer = new EthereumMessageSigner();
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(message, signature);

        if (string.Equals(recoveredAddress, request.Address, StringComparison.CurrentCultureIgnoreCase))
        {
            var accessToken = CreateToken(new User { Id = Guid.NewGuid(), Address = request.Address });
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
        
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtConfig.AccessTokenSecret));
        
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: credentials
        );
        
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        
        return jwt;
    }
    
    private static void SetCookie(string accessToken, HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append("XMN3bf8G9Vw3hSU", accessToken,
            new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(1)
            });
    }

    private static void RemoveCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete("XMN3bf8G9Vw3hSU",
            new CookieOptions
            {
                MaxAge = TimeSpan.Zero,
                Secure = true,
                SameSite = SameSiteMode.None
            });
    }
    private static byte[] GenerateSecureNonce(int length = 64)
    {
        var nonce = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(nonce);
        return nonce;
    }
}