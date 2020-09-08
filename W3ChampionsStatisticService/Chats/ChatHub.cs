using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.PadEvents.PadSync;

namespace W3ChampionsStatisticService.Chats
{
    public class ChatHub : Hub
    {
        private readonly ChatAuthenticationService _authenticationService;
        private readonly BanReadmodelRepository _banRepository;
        private readonly ConnectionMapping _connections;
        private readonly ChatHistory _chatHistory;

        public ChatHub(
            ChatAuthenticationService authenticationService,
            BanReadmodelRepository banRepository,
            ConnectionMapping connections,
            ChatHistory chatHistory)
        {
            _authenticationService = authenticationService;
            _banRepository = banRepository;
            _connections = connections;
            _chatHistory = chatHistory;
        }

        public async Task SendMessage(string chatApiKey, string battleTag, string message)
        {
            var trimmedMessage = message.Trim();
            var user = await _authenticationService.GetUser(chatApiKey, battleTag);
            if (!string.IsNullOrEmpty(trimmedMessage))
            {
                var chatRoom = _connections.GetRoom(Context.ConnectionId);
                var chatMessage = new ChatMessage(user, trimmedMessage);
                _chatHistory.AddMessage(chatRoom, chatMessage);
                await Clients.Group(chatRoom).SendAsync("ReceiveMessage", chatMessage);
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
            await Clients.Caller.SendAsync("StartChat", usersOfRoom, _chatHistory.GetMessages(chatRoom));
        }

        public async Task LoginAs(string chatApiKey, string battleTag, string chatRoom)
        {
            var user = await _authenticationService.GetUser(chatApiKey, battleTag);
            var ban = await _banRepository.GetBan(battleTag.ToLower());

            var nowDate = DateTime.Now.ToString("yyyy-MM-dd");
            if (ban != null && string.Compare(ban.endDate, nowDate, StringComparison.Ordinal) > 0)
            {
                await Clients.Caller.SendAsync("PlayerBannedFromChat", ban);
            }
            else
            {
                if (!user.VerifiedBattletag)
                {
                    await Clients.Caller.SendAsync("ChatKeyInvalid");
                }

                _connections.Add(Context.ConnectionId, chatRoom, user);
                await Groups.AddToGroupAsync(Context.ConnectionId, chatRoom);

                var usersOfRoom = _connections.GetUsersOfRoom(chatRoom);

                await Clients.Group(chatRoom).SendAsync("UserEntered", user);
                await Clients.Caller.SendAsync("StartChat", usersOfRoom, _chatHistory.GetMessages(chatRoom));
            }
        }
    }
}