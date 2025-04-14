namespace BondRun.Models;

public class AuthenticationRequest
{
    public string Address { get; set; }
    public string Signature { get; set; }
    public string Message { get; set; }
}