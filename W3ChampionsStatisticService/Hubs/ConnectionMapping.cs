using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Hubs;

public class ConnectionMapping
{
    private readonly Dictionary<string, WebSocketUser> _connections = new Dictionary<string, WebSocketUser>();

    public List<WebSocketUser> GetUsers()
    {
        lock (_connections)
        {
            return _connections.Values.Select(v => v).OrderBy(r => r.BattleTag).ToList();
        }
    }

    public void Add(string connectionId, WebSocketUser user)
    {
        lock (_connections)
        {
            if (!_connections.ContainsKey(connectionId))
            {
                _connections.Add(connectionId, user);
            }
        }
    }

    public WebSocketUser GetUser(string connectionId)
    {
        lock (_connections)
        {
            _connections.TryGetValue(connectionId, out WebSocketUser user);
            return user;
        }
    }

    public void Remove(string connectionId)
    {
        lock (_connections)
        {
            _connections.Remove(connectionId);
        }
    }
}
