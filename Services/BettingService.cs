using System.Diagnostics;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BondRun.Services;

public class BettingService : BackgroundService
{
    private const double TotalPixels = 290;
    private Stopwatch _gameStopwatch;
    private double _lastElapsedSeconds;
    private readonly TimeSpan _gameDuration = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _betTime = TimeSpan.FromSeconds(12);
    private readonly TimeSpan _delayAfterGame = TimeSpan.FromSeconds(5);
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
                if (_pixels["long"] < _pixels["shortX"])
                {
                    _pixels["long"] = Math.Clamp(_pixels["shortX"] + 5m, 0m, (decimal)TotalPixels);
                }
                break;
            case "short":
                if (_pixels["long"] > _pixels["shortX"])
                {
                    _pixels["shortX"] = Math.Clamp(_pixels["longX"] + 5m, 0m, (decimal)TotalPixels);
                }
                break;
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
                lock (_lock) { IsBettingOpen = true; }
                IsGameStarted = false;
                await _hub.Clients.All.SendAsync("BettingState", new {
                    IsBettingOpen,
                    IsGameStarted
                }, cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, _betTime.TotalSeconds, "Timer", cancellationToken: stoppingToken);

                lock (_lock) { IsBettingOpen = false; }
                IsGameStarted = true;
                await _hub.Clients.All.SendAsync("BettingState", new
                {
                    IsBettingOpen,
                    IsGameStarted
                },cancellationToken: stoppingToken);
                
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
                    
                    await Task.Delay(1, stoppingToken);
                }
                
                _cryptoPriceService.OnPriceChanged -= HandlePriceChanged;
                await timerTask;
                
                string gameResult = _prices[^1] == _prices[0] ? "tie" :
                    _prices[^1] > _prices[0] ? "long" : "short";
                
                NormalizeFinalPixels(gameResult);
                await _hub.Clients.All.SendAsync("RaceTick", new
                {
                    LongX  = _pixels["longX"],
                    ShortX = _pixels["shortX"]
                }, stoppingToken);
                
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
