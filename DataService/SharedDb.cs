using System.Collections.Concurrent;
using BondRun.Models;

namespace BondRun.DataService;

public class SharedDb
{
    private readonly ConcurrentDictionary<string, UserConnection> _connections;
    
    public ConcurrentDictionary<string, UserConnection> Connections => _connections;
}