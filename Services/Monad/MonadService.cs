using System.Diagnostics;
using System.Numerics;
using BondRun.Configuration;
using BondRun.Models.DTO;
using Microsoft.Extensions.Options;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace BondRun.Services.Monad;

public class MonadService : IMonadService
{
    private readonly EnvVariables _envVariables;
    private readonly ILogger<MonadService> _logger;
    private readonly Web3 _web3;
    private const int RequiredConfirmation = 12;
    private readonly TimeSpan _mempoolTime;
    public MonadService(IOptions<EnvVariables> envVariables, ILogger<MonadService> logger)
    {
        _envVariables = envVariables.Value;
        _logger = logger;
        _web3 = new(_envVariables.RpcUrl);
        _mempoolTime = TimeSpan.FromSeconds(5);
    }
    private async Task<bool> OnConnected() => await _web3.Net.Version.SendRequestAsync() == _envVariables.NetworkId;
    public async Task<bool> VerifyTransactionAsync(TransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await OnConnected())
        {
            _logger.LogError("OnConnected() returned false");
            return false;
        }

        var inMempool = await WaitForMempoolAsync(
            request.TransactionHash,
            cancellationToken);

        if (!inMempool)
        {
            _logger.LogError("WaitForMempool() returned false");
            return false;
        }

        var receipt = await _web3.Eth.Transactions
            .GetTransactionReceipt.SendRequestAsync(request.TransactionHash);

        if (!(string.Equals(receipt.From, request.AddressFrom, StringComparison.OrdinalIgnoreCase)
              && string.Equals(receipt.To, _envVariables.ContractAddress, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("from or to invalid address");
            return false;
        }
        
        var createdBlock = receipt.BlockNumber.Value;
        var latestBlock = (await _web3.Eth.Blocks.GetBlockNumber
            .SendRequestAsync()).Value;
            
        while (latestBlock - createdBlock <= RequiredConfirmation)
        {
            latestBlock = (await _web3.Eth.Blocks.GetBlockNumber
                .SendRequestAsync()).Value;
                
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }
            
        _logger.LogInformation("transaction confirmed");
        return true;
    }
    
    private async Task<bool> WaitForMempoolAsync(string txHash, CancellationToken cancellationToken = default)
    {
        var stopWatch = Stopwatch.StartNew();
        do
        {
            var txn = await _web3.Eth.Transactions
                .GetTransactionByHash
                .SendRequestAsync(txHash);
            
            if(txn != null)
                return true;

            await Task.Delay(200, cancellationToken);
        } while (stopWatch.Elapsed < _mempoolTime && !cancellationToken.IsCancellationRequested);
        
        return false;
    }
}