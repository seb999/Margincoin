using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace MarginCoin.Controllers
{

    public class AutoTrade4Controller : ControllerBase
    {
        private readonly BinanceClient _client;
        private readonly ClientWebSocket _socket;
        private readonly BinanceClientOptions _options;

        public AutoTrade4Controller(string apiKey, string secretKey)
        {
            //_options = new BinanceClientOptions();
            //_options.ApiCredentials.Key = apiKey;
            //_client = new BinanceClient(apiKey, secretKey);
            //_socket = new ClientWebSocket();
        }

        // public async Task Connect()
        // {
        //     await _socket.ConnectAsync(new Uri("wss://stream.binance.com:9443/ws/bnbbtc@trade"), CancellationToken.None);
        // }

        // public async Task StartListening()
        // {
        //     while (_socket.State == WebSocketState.Open)
        //     {
        //         var result = await _socket.ReceiveAsync(new ArraySegment<byte>(new byte[4096]), CancellationToken.None);
        //         if (result.MessageType == WebSocketMessageType.Text)
        //         {
        //             var message = System.Text.Encoding.UTF8.GetString(result.Array, 0, result.Count);
        //             Console.WriteLine(message);
        //         }
        //     }
        // }
    }

}