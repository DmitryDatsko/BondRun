namespace BondRun.Models;

public class Bet
{
    public required string ConnectionId { get; set; }
    public required decimal Amount { get; set; }
    public required string Side { get; set; }
}