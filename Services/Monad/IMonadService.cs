using BondRun.Models.DTO;

namespace BondRun.Services.Monad;

public interface IMonadService
{
    Task<bool> VerifyTransactionAsync(TransactionRequest request, CancellationToken cancellationToken = default);
}