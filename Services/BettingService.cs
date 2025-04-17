using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService : BackgroundService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(20);
    private readonly object _lock = new();
    private readonly List<decimal> _priceHistory = new();
    public bool IsBettingOpen { get; private set; }
    public BettingService(IHubContext<GameHub> hub, CryptoPriceService cryptoPriceService)
    {
        _hub = hub;
        _cryptoPriceService = cryptoPriceService;
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                lock (_lock) { IsBettingOpen = true; }
                await _hub.Clients.All.SendAsync("BettingStarted", new {
                    IsBettingOpen,
                    IsGameStarted = false
                }, cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, 5, "BetTimer", cancellationToken: stoppingToken);

                lock (_lock) { IsBettingOpen = false; }
                await _hub.Clients.All.SendAsync("BettingEnded", new
                {
                    IsBettingOpen,
                    IsGameStarted = true
                },cancellationToken: stoppingToken);

                var startPriceSnapshot = _cryptoPriceService.Price;
                _priceHistory.Clear();
                _priceHistory.Add(startPriceSnapshot);

                var bets = GameHub.GetAllBetsAndClear();
                
                var gameStopwatch = Stopwatch.StartNew();
                var gameDuration = TimeSpan.FromSeconds(10);
                var tickInterval = TimeSpan.FromMilliseconds(100);

                var startPrice = _cryptoPriceService.Price;
                var lastPrice = startPrice;
                var priceCheckInterval = TimeSpan.FromMilliseconds(500);
                var nextPriceCheck = DateTime.UtcNow + priceCheckInterval;

                double longY = 120, shortY = 120;
                double longSpeed = 0, shortSpeed = 0;
                double longTargetSpeed = 0, shortTargetSpeed = 0;

                var baseSpeed = 5.0;
                var speedMultiplier = 20.0;
                var smoothness = 0.1;

                var timerTask = RunCountdownAsync(gameStopwatch, gameDuration.TotalSeconds, "GameTimer", stoppingToken);

                while (gameStopwatch.Elapsed < gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var currentTime = DateTime.UtcNow;
                    
                    var currentPrice = _cryptoPriceService.Price;

                    _priceHistory.Add(currentPrice);

                    if (currentTime >= nextPriceCheck)
                    {
                        var priceDelta = currentPrice - startPrice;

                        longTargetSpeed = baseSpeed + (double)priceDelta * speedMultiplier;
                        shortTargetSpeed = baseSpeed - (double)priceDelta * speedMultiplier;

                        longTargetSpeed = Math.Clamp(longTargetSpeed, 2.0, 20.0);
                        shortTargetSpeed = Math.Clamp(shortTargetSpeed, 2.0, 20.0);

                        lastPrice = currentPrice;
                        nextPriceCheck = currentTime + priceCheckInterval;
                    }

                    longSpeed += (longTargetSpeed - longSpeed) * smoothness;
                    shortSpeed += (shortTargetSpeed - shortSpeed) * smoothness;

                    longY -= longSpeed * tickInterval.TotalSeconds;
                    shortY += shortSpeed * tickInterval.TotalSeconds;

                    longY = Math.Clamp(longY, 10, 230);
                    shortY = Math.Clamp(shortY, 10, 230);

                    await _hub.Clients.All.SendAsync("RaceTick", new {
                        LongY = longY,
                        ShortY = shortY
                    }, cancellationToken: stoppingToken);

                    await Task.Delay(tickInterval, stoppingToken);
                }

                await timerTask;
                
                var endPriceSnapshot = _cryptoPriceService.Price;
                bool isLong = endPriceSnapshot > startPriceSnapshot;
                string result = isLong ? "long" : "short";

                await _hub.Clients.All.SendAsync("GameResult", result, stoppingToken);
                
                foreach (var bet in bets)
                {
                    bool betOnLong = bet.Side == "long";
                    string outcome = betOnLong == isLong ? "win" : "lose";

                    await _hub.Clients.Client(bet.ConnectionId)
                        .SendAsync("BetResult", new {
                            BetResult = outcome,
                            IsBettingOpen,
                            IsGameStarted = false
                        }, cancellationToken: stoppingToken);
                }

                _priceHistory.Clear();
                
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}
