using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BondRun.Services.Logging;

public class LoggingGameState
{
    private readonly string _path;

    private readonly JsonSerializerOptions _options;

    public LoggingGameState()
    {
        _path = Path.Combine(Directory.GetCurrentDirectory(), "logs.json");
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, "[]");
        }
    }

    public void Append(object data)
    {
        var jsonText = File.ReadAllText(_path);
        var jsonArray = JsonNode.Parse(jsonText)?.AsArray() ?? new JsonArray();
        
        var node = JsonSerializer.SerializeToNode(data, data.GetType(), _options);
        jsonArray.Add(node);
        File.WriteAllText(_path, jsonArray.ToJsonString(_options));
    }
}