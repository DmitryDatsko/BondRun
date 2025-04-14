using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService : BackgroundService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(25);
    private readonly object _lock = new();
    public bool IsBettingOpen { get; private set; }

    public BettingService(IHubContext<GameHub> hub, CryptoPriceService cryptoPriceService)
    {
        _hub = hub;
        _cryptoPriceService = cryptoPriceService;
    }
    private async Task RunCountdownAsync(double totalSeconds, string methodName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        double lastSentTime = -1;

        while (stopwatch.Elapsed.TotalSeconds < totalSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double elapsed = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);

            if (elapsed != lastSentTime)
            {
                lastSentTime = elapsed;
                await _hub.Clients.All.SendAsync("Timer", elapsed, cancellationToken: cancellationToken);
            }
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

                await _hub.Clients.All.SendAsync("BettingStarted", cancellationToken: stoppingToken);
                await RunCountdownAsync(15, "BetTimer", cancellationToken: stoppingToken);
                
                lock (_lock) { IsBettingOpen = false; }

                await _hub.Clients.All.SendAsync("BettingEnded", cancellationToken: stoppingToken);
                
                var startPriceSnapshot = _cryptoPriceService.Price;

                var bets = GameHub.GetAllBetsAndClear();

                await RunCountdownAsync(totalSeconds:10, "GameTimer", cancellationToken: stoppingToken);
                
                var endPriceSnapshot = _cryptoPriceService.Price;

                bool isLong = endPriceSnapshot > startPriceSnapshot;
                string result = isLong ? "long" : "short";

                foreach (var bet in bets)
                {
                    bool betOnLong = bet.Side == "long";
                    string outcome = betOnLong == isLong ? "win" : "lose";

                    await _hub.Clients.Client(bet.ConnectionId)
                        .SendAsync("BetResult", result, outcome, cancellationToken: stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}
