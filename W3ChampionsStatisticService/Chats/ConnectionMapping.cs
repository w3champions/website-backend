using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Chats
{
    public class ConnectionMapping
    {
        private readonly Dictionary<string, ChatUser> _connections =
            new Dictionary<string, ChatUser>();

        public List<ChatUser> Users
        {
            get
            {
                lock (_connections)
                {
                    return _connections.Values.Select(v => v).ToList();
                }
            }
        }

        public void Add(string connectionId, ChatUser user)
        {
            lock (_connections)
            {
                if (!_connections.ContainsKey(connectionId))
                {
                    _connections.Add(connectionId, user);
                }
            }
        }

        public ChatUser GetUser(string key)
        {
            lock (_connections)
            {
                if (_connections.TryGetValue(key, out var userFound))
                {
                    return userFound;
                }
            }

            return null;
        }

        public void Remove(string connectionId)
        {
            lock (_connections)
            {
                if (_connections.ContainsKey(connectionId))
                {
                    _connections.Remove(connectionId);
                }
            }
        }
    }
}