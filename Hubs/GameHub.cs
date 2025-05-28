using System.Collections.Concurrent;
using BondRun.Data;
using BondRun.Models;
using BondRun.Models.DTO;
using BondRun.Services.Hub;
using BondRun.Services.Monad;
using BondRun.Services.Token;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BondRun.Hubs;

public sealed class GameHub(BettingService bettingService,
    IMonadService monadService,
    IUserIdentity userIdentity,
    IDbContextFactory<ApiDbContext> dbContextFactory) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("OnConnected", bettingService.ReadState());
        
        await base.OnConnectedAsync();
    }

    public async Task PlaceBet(
        Guid gameId,
        decimal amount,
        string side,
        string txHash)
    {
        var gameState = bettingService.ReadState();
        
        if (gameId != gameState.GameId)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty)
                .SendAsync("BetRejected", "This game has already finished.");
            return;
        }
        
        if (!gameState.IsBettingOpen || side is not ("long" or "short" or "tie"))
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync("BetRejected", "Bets are closed now");
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var userAddress = userIdentity.GetAddressByCookie();
        bool isBetPlaced = await db.Bets
            .AsNoTracking()
            .AnyAsync(b =>
                b.GameId == gameState.GameId &&
                b.UserAddress == userAddress);
        
        if (isBetPlaced)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync("BetRejected", "Already placed");
            return;
        }

        var receipt = await monadService.VerifyTransactionAsync(new TransactionRequest
            { TransactionHash = txHash, AddressFrom = userAddress });
        
        if (receipt == null)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync("BetRejected", "Transaction not verified");
            return;
        }

        await db.Bets.AddAsync(new Bet
        {
            Id = Guid.CreateVersion7(),
            Amount = amount,
            Side = side,
            UserAddress = userAddress,
            GameId = gameState.GameId
        });
        await db.SaveChangesAsync();

        await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync("BetAccepted");
    }
}