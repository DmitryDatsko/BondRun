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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                lock (_lock) { IsBettingOpen = true; }

                await _hub.Clients.All.SendAsync("BettingStarted", cancellationToken: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                lock (_lock) { IsBettingOpen = false; }

                await _hub.Clients.All.SendAsync("BettingEnded", cancellationToken: stoppingToken);
                
                var startPriceSnapshot = _cryptoPriceService.Price;

                var bets = GameHub.GetAllBetsAndClear();

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                
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
