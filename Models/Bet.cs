using System.ComponentModel.DataAnnotations;

namespace BondRun.Models;

public class Bet
{
    public Guid Id { get; set; }
    public required decimal Amount { get; set; }
    [MaxLength(12)] public required string Side { get; init; } 
    [MaxLength(128)] public required string UserAddress { get; init; }
    public Guid GameId { get; set; }

    public Game Game { get; set; } = null!;
}