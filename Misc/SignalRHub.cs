using MarginCoin.Class;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarginCoin.Misc
{
    public class SignalRHub : Hub
    {
        public async Task SendMessage(string message, string candleList, string order)
        {
            await Clients.All.SendAsync("ReceiveMessage", message, candleList, order);
        }

    }
}