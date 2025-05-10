using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService : BackgroundService
{
    private readonly TimeSpan _gameDuration = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _betTime = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _delayAfterGame = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _totalGameTime;
    private readonly object _lock = new();
    private readonly Dictionary<string, decimal> _pixels = new()
    {
        { "longX", 0m}, 
        { "shortX", 0m} 
    };
    private readonly List<decimal> _prices = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly ILogger<BettingService> _logger;
    public bool IsBettingOpen { get; private set; }
    public bool IsGameStarted { get; private set; }
    public BettingService(IHubContext<GameHub> hub, CryptoPriceService cryptoPriceService, ILogger<BettingService> logger)
    {
        _hub = hub;
        _cryptoPriceService = cryptoPriceService;
        _logger = logger;
        _totalGameTime = _gameDuration + _betTime + _delayAfterGame;
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
        double lastSentTime = -1;

        while (stopwatch.Elapsed.TotalSeconds < totalSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double elapsed = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);

            if (elapsed != lastSentTime)
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
            var delta = _prices[^1] - _prices[^2];

            if (delta > 0)
            {
                _pixels["longX"] += delta;
            }
            else if (delta < 0)
            {
                _pixels["shortX"] += Math.Abs(delta);
            }
            
            await _hub.Clients.All.SendAsync("RaceTick", new
            {
                LongX = _pixels["longX"],
                ShortX = _pixels["shortX"]
            });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(_totalGameTime);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                lock (_lock) { IsBettingOpen = true; }
                IsGameStarted = false;
                await _hub.Clients.All.SendAsync("BettingStarted", new {
                    IsBettingOpen,
                    IsGameStarted
                }, cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, _betTime.TotalSeconds, "BetTimer", cancellationToken: stoppingToken);

                lock (_lock) { IsBettingOpen = false; }
                IsGameStarted = true;
                await _hub.Clients.All.SendAsync("BettingEnded", new
                {
                    IsBettingOpen,
                    IsGameStarted
                },cancellationToken: stoppingToken);
                
                var gameStopwatch = Stopwatch.StartNew();
                
                var timerTask = RunCountdownAsync(gameStopwatch, _gameDuration.TotalSeconds, "GameTimer", stoppingToken);
                
                _cryptoPriceService.OnPriceChanged += HandlePriceChanged;
                
                var frameInterval = TimeSpan.FromMilliseconds(100);
                while (gameStopwatch.Elapsed < _gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    
                    _pixels["longX"] += 0.15m;
                    _pixels["shortX"] += 0.15m;
                    
                    await _hub.Clients.All.SendAsync("RaceTick", new
                    {
                        LongX = _pixels["longX"],
                        ShortX = _pixels["shortX"]
                    }, stoppingToken);
                    
                    await Task.Delay(frameInterval, stoppingToken);
                }
                
                _cryptoPriceService.OnPriceChanged -= HandlePriceChanged;
                await timerTask;
                
                string gameResult = _prices[^1] == _prices[0] ? "tie" :
                    _prices[^1] > _prices[0] ? "long" : "short";
                
                IsGameStarted = false;
                await _hub.Clients.All.SendAsync("GameResult", new
                {
                    gameResult,
                    IsBettingOpen,
                    IsGameStarted    
                }, stoppingToken);

                foreach (var bet in GameHub.GetAllBetsAndClear())
                {
                    string betResult = gameResult == bet.Side ? "win" : "lose";
                    
                    await _hub.Clients.Client(bet.ConnectionId).SendAsync("BetResult", new
                    {
                        BetResult = betResult
                    }, stoppingToken);
                }
                
                ClearPixelsDictionary();
                _prices.Clear();
                await Task.Delay(_delayAfterGame, stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogCritical($"Operation cancelled exception: {ex}");
        }
    }
}
