﻿using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService : BackgroundService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);
    private readonly object _lock = new();
    private readonly List<decimal> _priceHistory = new();
    private const int MovementMultiplier = 500_000;
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
                
                var timerTask = RunCountdownAsync(gameStopwatch, gameDuration.TotalSeconds, "GameTimer", stoppingToken);

                while (gameStopwatch.Elapsed < gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var currentPrice = _cryptoPriceService.Price;
                    var lastPrice = _priceHistory.Last();

                    if (currentPrice != lastPrice)
                    {
                        _priceHistory.Add(currentPrice);

                        var delta = (double)(currentPrice - lastPrice) / (double)startPriceSnapshot;
                        var signedLog = Math.Sign(delta) * Math.Log(1 + Math.Abs(delta));
                        var movement = signedLog * MovementMultiplier;

                        await _hub.Clients.All.SendAsync("RaceTick", movement, cancellationToken: stoppingToken);
                    }

                    //await Task.Delay(100, stoppingToken);
                }

                await timerTask;

                var endPriceSnapshot = _cryptoPriceService.Price;
                bool isLong = endPriceSnapshot > startPriceSnapshot;
                string result = isLong ? "long" : "short";

                foreach (var bet in bets)
                {
                    bool betOnLong = bet.Side == "long";
                    string outcome = betOnLong == isLong ? "win" : "lose";

                    await _hub.Clients.Client(bet.ConnectionId)
                        .SendAsync("BetResult", new {
                            GameResult = result,
                            BetResult = outcome,
                            IsBettingOpen,
                            IsGameStarted = false
                        }, cancellationToken: stoppingToken);
                }

                _priceHistory.Clear();
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}
