using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatHub : Hub
    {
        private readonly IBlizzardAuthenticationService _blizzardAuthenticationService;
        private readonly ConnectionMapping _connections;

        public ChatHub(
            IBlizzardAuthenticationService blizzardAuthenticationService,
            ConnectionMapping connections)
        {
            _blizzardAuthenticationService = blizzardAuthenticationService;
            _connections = connections;
        }

        public async Task SendMessage(string message, string bearer)
        {
            var trimmedMessage = message.Trim();
            var res = await _blizzardAuthenticationService.GetUser(bearer);
            if (res != null && !string.IsNullOrEmpty(trimmedMessage))
            {
                await Clients.All.SendAsync("ReceiveMessage", res.battletag, res.name, trimmedMessage);
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

        public async Task LoginAs(string bearer)
        {
            var res = await _blizzardAuthenticationService.GetUser(bearer);
            if (res != null)
            {
                var connectedUser = new ChatUser(res.battletag, res.name);
                _connections.Add(Context.ConnectionId, connectedUser);
                await Clients.Others.SendAsync("UserEntered", connectedUser.Name, connectedUser.BattleTag);
                await Clients.Caller.SendAsync("StartChat", _connections.Users);
            }
            else
            {
                await Clients.Caller.SendAsync("LoginFailed");
            }
        }
    }
}