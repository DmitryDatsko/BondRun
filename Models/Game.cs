using System.ComponentModel.DataAnnotations;

namespace BondRun.Models;

public class Game
{
    public Guid Id { get; init; }
    [MaxLength(32)] public string WinningSide { get; set; } = string.Empty;
    public DateTime PlayedAt { get; set; }
    public ICollection<Bet> Bets { get; set; } = new List<Bet>();
}