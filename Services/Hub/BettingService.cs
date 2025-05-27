using System.Diagnostics;
using BondRun.Data;
using BondRun.Hubs;
using BondRun.Models;
using BondRun.Models.DTO;
using BondRun.Services.Monad;
using BondRun.Services.Token;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Secp256k1;

namespace BondRun.Services.Hub;

public class BettingService : BackgroundService
{
    private const double TotalPixels = 290;
    private const decimal Margin = 0.05m;
    private readonly IDbContextFactory<ApiDbContext> _dbFactory;
    private Stopwatch _gameStopwatch;
    private double _lastElapsedSeconds;
    private readonly TimeSpan _gameDuration = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _betTime = TimeSpan.FromSeconds(12);
    private readonly TimeSpan _delayAfterGame = TimeSpan.FromSeconds(5);
    private readonly object _lock = new();
    private bool IsBettingOpen { get; set; }
    private bool IsGameStarted { get; set; }
    private Guid GameId { get; set; }
    private readonly Dictionary<string, decimal> _pixels = new()
    {
        { "longX", 0m}, 
        { "shortX", 0m} 
    };
    private readonly List<decimal> _prices = new();
    private readonly Dictionary<Bet, decimal> _payouts = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly ILogger<BettingService> _logger;
    private readonly IMonadService _monadService;
    public BettingService(IHubContext<GameHub> hub,
        CryptoPriceService cryptoPriceService, 
        ILogger<BettingService> logger,
        IDbContextFactory<ApiDbContext> dbFactory,
        IMonadService monadService)
    {
        _hub = hub;
        _cryptoPriceService = cryptoPriceService;
        _logger = logger;
        _dbFactory = dbFactory;
        _monadService = monadService;
    }

    private void UpdateState(bool openBetting, bool gameStarted, Guid gameId)
    {
        lock (_lock)
        {
            IsBettingOpen = openBetting;
            IsGameStarted = gameStarted;
            GameId = gameId;
        }
    }

    public BettingState ReadState()
    {
        lock (_lock)
        {
            return new BettingState
            {
                IsBettingOpen = IsBettingOpen,
                IsGameStarted = IsGameStarted,
                GameId = GameId
            };
        }
    }
    private void NormalizeFinalPixels(string gameResult)
    {
        switch (gameResult)
        {
            case "tie":
                if (_pixels["longX"] != _pixels["shortX"])
                {
                    _pixels["longX"] = Math.Max(_pixels["shortX"], _pixels["longX"]);
                    _pixels["shortX"] = _pixels["longX"];
                }
                break;
            case "long":
                if (_pixels["longX"] < _pixels["shortX"])
                {
                    _pixels["longX"] = Math.Clamp(_pixels["shortX"] + 20m, 0m, (decimal)TotalPixels);
                }
                break;
            case "short":
                if (_pixels["longX"] > _pixels["shortX"])
                {
                    _pixels["shortX"] = Math.Clamp(_pixels["longX"] + 20m, 0m, (decimal)TotalPixels);
                }
                break;
        }
    }

    private async Task PayoutCalculator(decimal totalPool, List<Bet> winningBets)
    {
        if (totalPool <= 0 || !winningBets.Any())
            return;
        
        var totalWinningStake = winningBets.Sum(b => b.Amount);
        
        var loserPool = totalPool - totalWinningStake;
        var poolForWinners = loserPool * (1 - Margin);

        foreach (var bet in winningBets)
        {
            decimal payout = bet.Amount;
            
            if (poolForWinners > 0 && totalWinningStake > 0)
            {
                var share = (payout / totalWinningStake) * poolForWinners;
                payout += share;
            }
            _payouts[bet] = payout;
        }
        
        await _monadService.AddWinnersBalance(_payouts);

        foreach (var payout in _payouts)
        {
            await _hub.Clients.User(payout.Key.UserAddress)
                .SendAsync("Payout", new { PayoutAmount = payout.Value });
        }
    }
    
    private void ClearPixelsDictionary()
    {
        foreach (var key in _pixels.Keys)
        {
            _pixels[key] = 0m;
        }
    }
    private async Task RunCountdownAsync(Stopwatch stopwatch, double totalSeconds, string methodName, CancellationToken cancellationToken = default)
    {
        const double eps = 0.005;
        double lastSentTime = -1;

        while (stopwatch.Elapsed.TotalSeconds < totalSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double elapsed = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);

            if (Math.Abs(elapsed - lastSentTime) > eps)
            {
                lastSentTime = elapsed;
                await _hub.Clients.All.SendAsync(methodName, elapsed, cancellationToken: cancellationToken);
            }

            await Task.Delay(50, cancellationToken);
        }

        await _hub.Clients.All.SendAsync(methodName, totalSeconds, cancellationToken: cancellationToken);
    }
    
    private void HandlePriceChanged(object? sender, decimal newPrice)
    {
        _ = CarSpeedOnPriceChange(newPrice)
            .ContinueWith(t =>
            {
                if(t.Exception != null) _logger.LogCritical($"Exception in price change handler: {t.Exception}");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }
    
    private async Task CarSpeedOnPriceChange(decimal newPrice)
    {
        _prices.Add(newPrice);

        if (_prices.Count > 2)
        {
            double elapsed = _gameStopwatch.Elapsed.TotalSeconds;
            double dt = elapsed - _lastElapsedSeconds;
            _lastElapsedSeconds = elapsed;
            
            decimal priceDelta = _prices[^1] - _prices[^2];
            decimal rawPx = Math.Abs(priceDelta);
            
            decimal maxStep = (decimal)(TotalPixels * (dt / _gameDuration.TotalSeconds));
            decimal movePx = Math.Round(Math.Clamp(rawPx, 0, maxStep), 2);
            
            if(priceDelta > 0) _pixels["longX"] += movePx;
            if (priceDelta < 0) _pixels["shortX"] += movePx;
            
            await _hub.Clients.All.SendAsync("RaceTick", new
            {
                LongX = _pixels["longX"],
                ShortX = _pixels["shortX"]
            });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
                UpdateState(true, false, Guid.CreateVersion7());
                
                await db.Games.AddAsync(new Game
                {
                    Id = GameId
                }, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);
                
                await _hub.Clients.All.SendAsync("BettingState", 
                    ReadState(),
                    cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, _betTime.TotalSeconds, "Timer", cancellationToken: stoppingToken);
                
                UpdateState(false, true, GameId);
                
                await _hub.Clients.All.SendAsync("BettingState", 
                    ReadState(),
                    cancellationToken: stoppingToken);
                
                _gameStopwatch = Stopwatch.StartNew();
                _lastElapsedSeconds = 0.0;
                
                var timerTask = RunCountdownAsync(_gameStopwatch, _gameDuration.TotalSeconds, "Timer", stoppingToken);
                
                _cryptoPriceService.OnPriceChanged += HandlePriceChanged;

                var frameIntervalMs = 100.0;
                var nextTickMs = frameIntervalMs;
                
                while (_gameStopwatch.Elapsed < _gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var elapsedMs = _gameStopwatch.Elapsed.TotalMilliseconds;
                    if (elapsedMs >= nextTickMs)
                    {
                        do
                        {
                            _pixels["longX"] += 0.15m;
                            _pixels["shortX"] += 0.15m;
                            nextTickMs += frameIntervalMs;
                        }
                        while (elapsedMs >= nextTickMs);
                        
                        await _hub.Clients.All.SendAsync("RaceTick", new
                        {
                            LongX  = _pixels["longX"],
                            ShortX = _pixels["shortX"]
                        }, stoppingToken);
                    }
                    
                    await Task.Delay(100, stoppingToken);
                }
                
                await timerTask;
                _cryptoPriceService.OnPriceChanged -= HandlePriceChanged;
                
                string gameResult = _prices[^1] == _prices[0] ? "tie" :
                    _prices[^1] > _prices[0] ? "long" : "short";
                
                NormalizeFinalPixels(gameResult);
                await _hub.Clients.All.SendAsync("RaceTick", new
                {
                    LongX  = _pixels["longX"],
                    ShortX = _pixels["shortX"]
                }, stoppingToken);
                
                UpdateState(true, false, GameId);
                var state = ReadState();
                
                await _hub.Clients.All.SendAsync("GameResult", new
                {
                    gameResult,
                    state.IsBettingOpen,
                    state.IsGameStarted,
                    state.GameId
                }, stoppingToken);

                var winningBets = await db.Bets
                    .AsNoTracking()
                    .Where(b => b.GameId == GameId &&
                                b.Side == gameResult)
                    .ToListAsync(stoppingToken);
                
                var totalPool = await db.Bets.AsNoTracking()
                    .Where(b => b.GameId == GameId)
                    .SumAsync(b => b.Amount, stoppingToken);
                
                await PayoutCalculator(totalPool, winningBets);
                
                ClearPixelsDictionary();
                _prices.Clear();
                _payouts.Clear();
                await Task.Delay(_delayAfterGame, stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogCritical($"Operation cancelled exception: {ex}");
        }
    }
}
