using System.Collections.Concurrent;
using BondRun.Models;
using BondRun.Models.DTO;
using BondRun.Services.Hub;
using BondRun.Services.Monad;
using BondRun.Services.Token;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Hubs;

public sealed class GameHub(BettingService bettingService, IMonadService monadService, IUserIdentity userIdentity) : Hub
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
    public async Task PlaceBet(decimal amount, string side, string txHash)
    {
        if (!bettingService.IsBettingOpen || side is not ("long" or "short" or "tie"))
        {
            await Clients.Caller.SendAsync("BetRejected", "Bets are closed now");
            return;
        }

        var userAddress = userIdentity.GetAddressByCookie();
        
        if (!await monadService.VerifyTransactionAsync(new TransactionRequest
                { TransactionHash = txHash, AddressFrom = userAddress }))
        {
            await Clients.Caller.SendAsync("BetRejected", "Transaction not verified");
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