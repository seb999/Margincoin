using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace MarginCoin.Misc
{
    public class SignalRHub : Hub
    {
        // public async Task SendMessage(string user, string message)
        // {
        //     await Clients.All.SendAsync("ReceiveMessage", user, message);
        // }
    }
}