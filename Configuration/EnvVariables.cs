namespace BondRun.Configuration;

public class EnvVariables
{
    public string RpcUrl { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string JwtTokenSecret { get; set; } = string.Empty;
    public string NetworkId { get; set; } = string.Empty;
    public string ContractAddress { get; set; } = string.Empty;
    public string CookieName { get; set; } = string.Empty;
}