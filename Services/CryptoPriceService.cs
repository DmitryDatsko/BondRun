using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json.Linq;

namespace BondRun.Services;

public class CryptoPriceService : BackgroundService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ClientWebSocket _client = new();
    private static readonly Uri Uri = new("wss://stream.bybit.com/v5/public/spot");
    private const string Topic = "tickers.BTCUSDT";

    public decimal Price { get; private set; }
    
    public CryptoPriceService(IHubContext<GameHub> hub) => _hub = hub;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _client.ConnectAsync(Uri, stoppingToken);

            string subscribeMessage = @"{
                ""op"": ""subscribe"",
                ""args"": [""tickers.BTCUSDT""]
            }";

            await _client.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes(subscribeMessage)),
                WebSocketMessageType.Text,
                true, CancellationToken.None);

            var buffer = new byte[8192];

            while (_client.State == WebSocketState.Open)
            {
                var result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    JObject json = JObject.Parse(message);

                    if (json["topic"]?.ToString() == "tickers.BTCUSDT")
                    {
                        var price = json["data"]?["lastPrice"]?.ToString();
                        Price = decimal.TryParse(price, CultureInfo.InvariantCulture, out var p) ? p : 0;
                        //await _hub.Clients.All.SendAsync("NewPrice", price, cancellationToken: stoppingToken);
                    }
                }

            }
        }
        finally
        {
            if (_client.State == WebSocketState.Open || _client.State == WebSocketState.CloseReceived)
            {
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", stoppingToken);
            }

            _client.Dispose();
        }
    }
}