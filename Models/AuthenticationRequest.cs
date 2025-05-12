namespace BondRun.Models;

public class AuthenticationRequest
{
    public required string Address { get; set; }
    public required string Signature { get; set; }
    public required string Message { get; set; }
}