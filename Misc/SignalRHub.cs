using MarginCoin.Class;
using MarginCoin.Service;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarginCoin.Misc
{
    public class SignalRHub : Hub
    {
        private ILogger _logger;

        public SignalRHub(ILogger<MLService> logger)
        {
            _logger = logger;
        }

        public async Task SendMessage(string message, string data)
        {
            await Clients.All.SendAsync("ReceiveMessage", message, data);
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogError("THE SIGNAL CLIENT IS DISCONNECTED");
            // Add your own code here.
            // For example: in a chat application, mark the user as offline, 
            // delete the association between the current connection id and user name.
            return Task.CompletedTask;
        }

    }
}