using System.Text.Json;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Where the shell keeps its small persisted preferences, read and written by string key.</summary>
public interface IShellStorage
{
    /// <summary>Returns the persisted value for a key, or null when no value exists.</summary>
    Task<string?> GetAsync(string key);
    /// <summary>Persists a string value under the supplied key.</summary>
    Task SetAsync(string key, string value);
}
/// <summary>Shell storage that holds values in memory only, for tests and hosts with no persistence.</summary>
public sealed class InMemoryShellStorage : IShellStorage
{
    private readonly Dictionary<string,string> data=new();
    /// <summary>Returns the stored value for the key, or null when nothing is stored.</summary>
    public Task<string?> GetAsync(string key)=>Task.FromResult(data.TryGetValue(key,out var value)?value:null);
    /// <summary>Stores a value under the key.</summary>
    public Task SetAsync(string key,string value){data[key]=value;return Task.CompletedTask;}
}
/// <summary>Reads and writes the shell persisted preferences, ignoring storage that is missing or fails.</summary>
public static class ShellPersistence
{
    /// <summary>Builds a versioned storage key from the shell id and further parts.</summary>
    public static string Key(string shellId,params string[] parts)=>string.Join(':',new[]{"hlp-app-shell:v1",shellId}.Concat(parts));
    /// <summary>Reads a stored value of a struct type, or null when storage is absent, empty or unreadable.</summary>
    public static async Task<T?> ReadAsync<T>(IShellStorage? storage,string key) where T:struct{if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;return doc.RootElement.GetProperty("value").Deserialize<T>();}catch{return null;}}
    /// <summary>Reads a stored array of strings, or null when storage is absent, empty or unreadable.</summary>
    public static async Task<string[]?> ReadArrayAsync(IShellStorage? storage,string key){if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;var value=doc.RootElement.GetProperty("value");return value.ValueKind==System.Text.Json.JsonValueKind.Array&&value.EnumerateArray().All(e=>e.ValueKind==System.Text.Json.JsonValueKind.String)?value.EnumerateArray().Select(e=>e.GetString()!).ToArray():null;}catch{return null;}}
    /// <summary>Reads a stored string, or null when storage is absent, empty or unreadable.</summary>
    public static async Task<string?> ReadStringAsync(IShellStorage? storage,string key){if(storage is null)return null;try{var raw=await storage.GetAsync(key);if(raw is null)return null;using var doc=System.Text.Json.JsonDocument.Parse(raw);if(!doc.RootElement.TryGetProperty("v",out var v)||v.GetInt32()!=1)return null;var value=doc.RootElement.GetProperty("value");return value.ValueKind==System.Text.Json.JsonValueKind.String?value.GetString():null;}catch{return null;}}
    /// <summary>Serialises a value to JSON and stores it without waiting for the write.</summary>
    public static void Write(IShellStorage? storage,string key,object value){if(storage is null)return;_ = storage.SetAsync(key,System.Text.Json.JsonSerializer.Serialize(new{v=1,value})).ContinueWith(_=>{},TaskContinuationOptions.OnlyOnFaulted);}
}
