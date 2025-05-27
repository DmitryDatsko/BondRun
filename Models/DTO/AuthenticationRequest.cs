namespace BondRun.Models.DTO;

public class AuthenticationRequest
{
    public required string Signature { get; set; }
    public required string Message { get; set; }
}