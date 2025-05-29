namespace BondRun.Models.DTO;

public class BetDto
{
    public required decimal Amount { get; set; }
    public required string Side { get; set; }
    public required string UserAddress { get; set; }
}