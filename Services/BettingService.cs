using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService(IHubContext<GameHub> hub, CryptoPriceService cryptoPriceService)
    : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(20);
    private readonly object _lock = new();
    private readonly Dictionary<string, decimal> _pixels = new()
    {
        { "longX", 0m}, 
        { "shortX", 0m} 
    };
    private readonly List<decimal> _prices = new();
    public bool IsBettingOpen { get; private set; }
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
                await hub.Clients.All.SendAsync(methodName, elapsed, cancellationToken: cancellationToken);
            }

            await Task.Delay(50, cancellationToken);
        }

        await hub.Clients.All.SendAsync(methodName, totalSeconds, cancellationToken: cancellationToken);
    }
    private void HandlePriceChanged(object? sender, decimal newPrice)
    {
        _ = CarSpeedOnPriceChange(newPrice)
            .ContinueWith(t =>
            {
                if(t.Exception != null) Console.Error.WriteLine($"Exception in price change handler: {t.Exception}");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }
    private async Task CarSpeedOnPriceChange(decimal newPrice)
    {
        _prices.Add(newPrice);
        
        if (_prices.Count > 2)
        {
            var delta = _prices[^1] - _prices[^2];
            var movement = Math.Abs(delta) * 8.5m;
            
            if(delta >= 0)
            {
                _pixels["longX"] += movement;
            }
            else
            {
                _pixels["shortX"] += movement;
            }

            await hub.Clients.All.SendAsync("RaceTick", new
            {
                LongX = _pixels["longX"],
                ShortX = _pixels["shortX"]
            });
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                lock (_lock) { IsBettingOpen = true; }
                await hub.Clients.All.SendAsync("BettingStarted", new {
                    IsBettingOpen,
                    IsGameStarted = false
                }, cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, 5, "BetTimer", cancellationToken: stoppingToken);

                lock (_lock) { IsBettingOpen = false; }
                await hub.Clients.All.SendAsync("BettingEnded", new
                {
                    IsBettingOpen,
                    IsGameStarted = true
                },cancellationToken: stoppingToken);

                
                var gameStopwatch = Stopwatch.StartNew();
                var gameDuration = TimeSpan.FromSeconds(10);
                
                var timerTask = RunCountdownAsync(gameStopwatch, gameDuration.TotalSeconds, "GameTimer", stoppingToken);
                
                cryptoPriceService.OnPriceChanged += HandlePriceChanged;
                
                var frameInterval = TimeSpan.FromMilliseconds(100);
                while (gameStopwatch.Elapsed < gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    
                    _pixels["longX"] += 0.15m;
                    _pixels["shortX"] += 0.15m;
                    
                    await hub.Clients.All.SendAsync("RaceTick", new
                    {
                        LongX = _pixels["longX"],
                        ShortX = _pixels["shortX"]
                    }, stoppingToken);
                    
                    await Task.Delay(frameInterval, stoppingToken);
                }
                
                cryptoPriceService.OnPriceChanged -= HandlePriceChanged;
                await timerTask;
                
                string gameResult = _prices[^1] > _prices[0] ? "long" : "short";
                
                await hub.Clients.All.SendAsync("GameResult", new
                {
                    gameResult,
                    IsBettingOpen,
                    IsGameStarted = false
                }, stoppingToken);

                foreach (var bet in GameHub.GetAllBetsAndClear())
                {
                    string betResult = gameResult == bet.Side ? "win" : "lose";
                    
                    await hub.Clients.Client(bet.ConnectionId).SendAsync("BetResult", new
                    {
                        BetResult = betResult
                    }, stoppingToken);
                }
                
                ClearPixelsDictionary();
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}
