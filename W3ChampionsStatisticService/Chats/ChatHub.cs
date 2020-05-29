using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatHub : Hub
    {
        private readonly ChatAuthenticationService _authenticationService;
        private readonly ConnectionMapping _connections;

        public ChatHub(
            ChatAuthenticationService authenticationService,
            ConnectionMapping connections)
        {
            _authenticationService = authenticationService;
            _connections = connections;
        }

        public async Task SendMessage(string message, string chatApiKey)
        {
            var trimmedMessage = message.Trim();
            var res = await _authenticationService.GetUser(chatApiKey);
            if (res != null && !string.IsNullOrEmpty(trimmedMessage))
            {
                await Clients.All.SendAsync("ReceiveMessage", res.BattleTag, res.Name, trimmedMessage);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var user = _connections.GetUser(Context.ConnectionId);
            if (user != null)
            {
                _connections.Remove(Context.ConnectionId);
                await Clients.All.SendAsync("UserLeft", user.BattleTag);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task LoginAs(string chatApiKey)
        {
            var res = await _authenticationService.GetUser(chatApiKey);
            if (res != null)
            {
                _connections.Add(Context.ConnectionId, res);
                await Clients.Others.SendAsync("UserEntered", res.Name, res.BattleTag);
                await Clients.Caller.SendAsync("StartChat", _connections.Users);
            }
            else
            {
                await Clients.Caller.SendAsync("LoginFailed");
            }
        }
    }
}