namespace BondRun.Models;

public class Bet
{
    public Guid Id { get; set; }
    public required decimal Amount { get; set; }
    public required string Side { get; init; } 
    public required string UserAddress { get; init; }
    public Guid GameId { get; set; }

    public Game Game { get; set; } = null!;
}