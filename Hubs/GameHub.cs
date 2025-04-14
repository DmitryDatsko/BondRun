using System.Collections.Concurrent;
using BondRun.Models;
using BondRun.Services;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Hubs;

public sealed class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, Bet> _bets = new();
    private readonly BettingService _bettingService;

    public GameHub(BettingService bettingService) => _bettingService = bettingService;

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("OnConnected");
        
        await base.OnConnectedAsync();
    }

    public async Task PlaceBet(decimal amount, string side)
    {
        if (!_bettingService.IsBettingOpen)
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

        _bets.TryAdd(Context.ConnectionId, bet);
        
        await Clients.Caller.SendAsync("BetAccepted", bet);
    }

    public static List<Bet> GetAllBetsAndClear()
    {
        var list = _bets.Values.ToList();
        _bets.Clear();
        return list;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _bets.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}