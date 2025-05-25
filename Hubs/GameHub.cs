using System.Collections.Concurrent;
using BondRun.Models;
using BondRun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Hubs;

public sealed class GameHub(BettingService bettingService) : Hub
{
    private static readonly ConcurrentDictionary<string, Bet> Bets = new();
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("OnConnected", new
        {
            bettingService.IsBettingOpen,
            bettingService.IsGameStarted
        });
        
        await base.OnConnectedAsync();
    }
    
    public async Task PlaceBet(decimal amount, string side, string walletAddress)
    {
        if (!bettingService.IsBettingOpen || bettingService.IsGameStarted 
                                          || side is not ("long" or "short" or "tie"))
        {
            await Clients.Caller.SendAsync("BetRejected", "Bets are closed now");
            return;
        }

        var bet = new Bet
        {
            ConnectionId = Context.ConnectionId,
            Amount = amount,
            Side = side
        };

        Bets.TryAdd(Context.ConnectionId, bet);
        
        await Clients.Caller.SendAsync("BetAccepted", bet);
    }
    
    public static List<Bet> GetAllBetsAndClear()
    {
        var list = Bets.Values.ToList();
        Bets.Clear();
        return list;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Bets.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}