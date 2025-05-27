namespace BondRun.Models;

public class Game
{
    public Guid Id { get; set; }
    public string WinningSide { get; set; } = string.Empty;
    
    public ICollection<Bet> Bets { get; set; } = new List<Bet>();
}