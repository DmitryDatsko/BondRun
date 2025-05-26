namespace BondRun.Configuration;

public class JwtConfig
{
    public const string SectionName = "JwtConfiguration";
    public string AccessTokenSecret { get; set; } = string.Empty;
}