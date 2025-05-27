using System.Diagnostics;
using System.Numerics;
using BondRun.Configuration;
using BondRun.Models;
using BondRun.Models.Contracts.Functions;
using BondRun.Models.DTO;
using Microsoft.Extensions.Options;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace BondRun.Services.Monad;

public class MonadService : IMonadService
{
    private readonly EnvVariables _envVariables;
    private readonly ILogger<MonadService> _logger;
    private readonly Web3 _web3;
    private readonly Web3 _web3WithSigner;
    private const int RequiredConfirmation = 12;
    private readonly TimeSpan _mempoolTime;
    public MonadService(IOptions<EnvVariables> envVariables, ILogger<MonadService> logger)
    {
        _envVariables = envVariables.Value;
        _logger = logger;
        _web3 = new(_envVariables.RpcUrl);
        
        var account = new Account(_envVariables.PrivateKey);
        _web3WithSigner = new Web3(account, _envVariables.RpcUrl);
        
        _mempoolTime = TimeSpan.FromSeconds(5);
    }
    private async Task<bool> OnConnected() => await _web3.Net.Version.SendRequestAsync() == _envVariables.NetworkId;
    public async Task<TransactionReceipt?> VerifyTransactionAsync(TransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await OnConnected())
        {
            _logger.LogError("OnConnected() returned false");
            return null;
        }

        var inMempool = await WaitForMempoolAsync(
            request.TransactionHash,
            cancellationToken);

        if (!inMempool)
        {
            _logger.LogError("WaitForMempool() returned false");
            return null;
        }

        var receipt = await _web3.Eth.Transactions
            .GetTransactionReceipt.SendRequestAsync(request.TransactionHash);

        if (!(string.Equals(receipt.From, request.AddressFrom, StringComparison.OrdinalIgnoreCase)
              && string.Equals(receipt.To, _envVariables.ContractAddress, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("from or to invalid address");
            return null;
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
        
        return receipt;
    }
    
    public async Task AddWinnersBalance(Dictionary<Bet, decimal> payouts,
        CancellationToken cancellationToken = default)
    {
        var handler = _web3WithSigner.Eth.GetContractTransactionHandler<AddWinnersBalanceFunction>();
        
        foreach (var batchDict in ChuckDictionary(payouts, 200))
        {
            var users = batchDict.Select(kv => new User
            {
                UserAddress = kv.Key.UserAddress,
                Amount = Web3.Convert.ToWei(kv.Value)
            }).ToList();

            var function = new AddWinnersBalanceFunction()
            {
                Banch = users
            };
            
            var receipt = await handler
                .SendRequestAndWaitForReceiptAsync(
                    _envVariables.ContractAddress,
                    function,
                    cancellationToken);
            
            if(receipt.Status.Value == 0)
                _logger.LogError($"Batch failed: {receipt.TransactionHash}");
            
            _logger.LogInformation("Batch of {Count} users sent in Tx {TxHash}",
                users.Count, receipt.TransactionHash);
        }
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

    private static IEnumerable<Dictionary<TKey, TValue>> ChuckDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> source,
        int chunkSize) where TKey : notnull
    {
        var items = source.AsEnumerable();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            yield return items
                .Skip(i)
                .Take(chunkSize)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}