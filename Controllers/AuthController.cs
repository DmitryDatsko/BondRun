using BondRun.Models;
using Microsoft.AspNetCore.Mvc;
using Nethereum.Signer;

namespace BondRun.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    [HttpPost]
    public IActionResult Authenticate([FromBody] AuthenticationRequest request)
    {
        var message = request.Message;
        var signature = request.Signature;

        var signer = new EthereumMessageSigner();
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(message, signature);

        if (string.Equals(recoveredAddress, request.Address, StringComparison.CurrentCultureIgnoreCase))
        {
            return Ok(new { message = "Authentication successful" });
        }

        return Unauthorized(new { message = "Invalid signature" });
    }
}