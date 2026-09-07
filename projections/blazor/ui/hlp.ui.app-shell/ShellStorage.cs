using System.Text.Json;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public interface IShellStorage { Task<string?> GetAsync(string key); Task SetAsync(string key, string value); }
public sealed class InMemoryShellStorage : IShellStorage
{
    private readonly Dictionary<string,string> data=new();
    public Task<string?> GetAsync(string key)=>Task.FromResult(data.TryGetValue(key,out var value)?value:null);
    public Task SetAsync(string key,string value){data[key]=value;return Task.CompletedTask;}
}
public static class ShellPersistence
{
    public static string Key(string shellId,params string[] parts)=>string.Join(':',new[]{"hlp-app-shell:v1",shellId}.Concat(parts));
    public static async Task<T?> ReadAsync<T>(IShellStorage? storage,string key) where T:struct{if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;return doc.RootElement.GetProperty("value").Deserialize<T>();}catch{return null;}}
    public static async Task<string[]?> ReadArrayAsync(IShellStorage? storage,string key){if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;var value=doc.RootElement.GetProperty("value");return value.ValueKind==System.Text.Json.JsonValueKind.Array&&value.EnumerateArray().All(e=>e.ValueKind==System.Text.Json.JsonValueKind.String)?value.EnumerateArray().Select(e=>e.GetString()!).ToArray():null;}catch{return null;}}
    public static async Task<string?> ReadStringAsync(IShellStorage? storage,string key){if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;var value=doc.RootElement.GetProperty("value");return value.ValueKind==System.Text.Json.JsonValueKind.String?value.GetString():null;}catch{return null;}}
    public static void Write(IShellStorage? storage,string key,object value){if(storage is null)return;_ = storage.SetAsync(key,System.Text.Json.JsonSerializer.Serialize(new{v=1,value})).ContinueWith(_=>{},TaskContinuationOptions.OnlyOnFaulted);}
}
