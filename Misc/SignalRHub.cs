using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace MarginCoin.Misc
{
    public class SignalRHub : Hub
    {
        public async Task SendMessage(string message, double rsi, double R1, double S1)
        {
            await Clients.All.SendAsync("ReceiveMessage", message, rsi, R1, S1);
        }
    }
}