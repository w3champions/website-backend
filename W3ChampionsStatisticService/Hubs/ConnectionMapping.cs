using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace W3ChampionsStatisticService.Hubs;

public class ConnectionMapping
{
    private readonly Dictionary<string, WebSocketUser> _connections = new();
    private readonly Dictionary<string, HashSet<string>> _onlineUsersByBattleTag = new();
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    public List<WebSocketUser> GetUsers()
    {
        _lock.EnterReadLock();
        try
        {
            return _connections.Values.Select(v => v).OrderBy(r => r.BattleTag).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Add(string connectionId, WebSocketUser user)
    {
        _lock.EnterWriteLock();
        try
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
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public WebSocketUser GetUser(string connectionId)
    {
        _lock.EnterReadLock();
        try
        {
            _connections.TryGetValue(connectionId, out WebSocketUser user);
            return user;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public HashSet<string> GetConnectionId(string battleTag)
    {
        _lock.EnterReadLock();
        try
        {
            // It's possible for a single user to have multiple websocket connections,
            if (_onlineUsersByBattleTag.TryGetValue(battleTag, out var connections))
            {
                return connections;
            }
            return new HashSet<string>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool IsUserOnline(string battleTag)
    {
        _lock.EnterReadLock();
        try
        {
            return _onlineUsersByBattleTag.ContainsKey(battleTag) && _onlineUsersByBattleTag[battleTag].Count > 0;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Remove(string connectionId)
    {
        _lock.EnterWriteLock();
        try
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
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
