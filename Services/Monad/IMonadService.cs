using BondRun.Models;
using BondRun.Models.DTO;
using Nethereum.RPC.Eth.DTOs;

namespace BondRun.Services.Monad;

public interface IMonadService
{
    Task<TransactionReceipt?> VerifyTransactionAsync(TransactionRequest request, CancellationToken cancellationToken = default);
    Task AddWinnersBalance(Dictionary<Bet, decimal> payouts, CancellationToken cancellationToken = default);
}