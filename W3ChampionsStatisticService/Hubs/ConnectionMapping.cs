using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Hubs;

public class ConnectionMapping
{
    private readonly Dictionary<string, WebSocketUser> _connections = new();
    private readonly Dictionary<string, HashSet<string>> _onlineUsersByBattleTag = new();
    private readonly object _lock = new();

    public List<WebSocketUser> GetUsers()
    {
        lock (_lock)
        {
            return _connections.Values.Select(v => v).OrderBy(r => r.BattleTag).ToList();
        }
    }

    public void Add(string connectionId, WebSocketUser user)
    {
        lock (_lock)
        {
            if (!_connections.ContainsKey(connectionId))
            {
                _connections.Add(connectionId, user);
                if (!_onlineUsersByBattleTag.ContainsKey(user.BattleTag))
                {
                    _onlineUsersByBattleTag[user.BattleTag] = new HashSet<string>();
                }
                _onlineUsersByBattleTag[user.BattleTag].Add(connectionId);
            }
        }
    }

    public WebSocketUser GetUser(string connectionId)
    {
        lock (_lock)
        {
            _connections.TryGetValue(connectionId, out WebSocketUser user);
            return user;
        }
    }

    public HashSet<string> GetConnectionId(string battleTag)
    {
        lock (_lock)
        {
            // It's possible for a single user to have multiple websocket connections,
            if (_onlineUsersByBattleTag.TryGetValue(battleTag, out var connections))
            {
                return connections;
            }
            return new HashSet<string>();
        }
    }

    public bool IsUserOnline(string battleTag)
    {
        lock (_lock)
        {
            return _onlineUsersByBattleTag.ContainsKey(battleTag) && _onlineUsersByBattleTag[battleTag].Count > 0;
        }
    }

    public void Remove(string connectionId)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(connectionId, out var user))
            {
                _connections.Remove(connectionId);

                if (_onlineUsersByBattleTag.TryGetValue(user.BattleTag, out var userConnections))
                {
                    userConnections.Remove(connectionId);
                    if (userConnections.Count == 0)
                    {
                        _onlineUsersByBattleTag.Remove(user.BattleTag);
                    }
                }
            }
        }
    }
}
