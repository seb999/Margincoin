using MarginCoin.Class;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarginCoin.Misc
{
    public class SignalRHub : Hub
    {
        public async Task SendMessage(string message, RsiCandle[] symbolWeight, double rsi, double R1, double S1)
        {
            await Clients.All.SendAsync("ReceiveMessage", message, symbolWeight, rsi, R1, S1);
        }
        
    }
}