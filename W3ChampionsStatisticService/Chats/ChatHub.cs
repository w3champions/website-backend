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

        public async Task SendMessage(string chatApiKey, string battleTag, string message)
        {
            var trimmedMessage = message.Trim();
            var user = await _authenticationService.GetUser(chatApiKey, battleTag);
            if (!string.IsNullOrEmpty(trimmedMessage))
            {
                var chatRoom = _connections.GetRoom(Context.ConnectionId);
                await Clients.Group(chatRoom).SendAsync("ReceiveMessage", new UserDto(user.Name, user.BattleTag, user.VerifiedBattletag), trimmedMessage);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var user = _connections.GetUser(Context.ConnectionId);
            if (user != null)
            {
                var chatRoom = _connections.GetRoom(Context.ConnectionId);
                _connections.Remove(Context.ConnectionId);
                await Clients.Group(chatRoom).SendAsync("UserLeft", user);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SwitchRoom(string chatApiKey, string battleTag, string chatRoom)
        {
            var user = await _authenticationService.GetUser(chatApiKey, battleTag);

            var oldRoom = _connections.GetRoom(Context.ConnectionId);
            _connections.Remove(Context.ConnectionId);
            _connections.Add(Context.ConnectionId, chatRoom, user);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldRoom);
            await Groups.AddToGroupAsync(Context.ConnectionId, chatRoom);

            var usersOfRoom = _connections.GetUsersOfRoom(chatRoom);
            await Clients.Group(oldRoom).SendAsync("UserLeft", user);
            await Clients.Group(chatRoom).SendAsync("UserEntered", user);
            await Clients.Caller.SendAsync("StartChat", usersOfRoom);
        }



        public async Task LoginAs(string chatApiKey, string battleTag, string chatRoom)
        {
            var user = await _authenticationService.GetUser(chatApiKey, battleTag);

            _connections.Add(Context.ConnectionId, chatRoom, user);
            await Groups.AddToGroupAsync(Context.ConnectionId, chatRoom);

            var usersOfRoom = _connections.GetUsersOfRoom(chatRoom);

            await Clients.Group(chatRoom).SendAsync("UserEntered", user);
            await Clients.Caller.SendAsync("StartChat", usersOfRoom);
        }
    }
}