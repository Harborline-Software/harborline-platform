using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// bUnit records the module calls but runs none of layers-rail.js, so these helpers run the shipped module
// under Node against stand-in elements and hand back what it asked the DOM for: the selector focusNode
// focuses, and the selectors and preventDefault the tree keydown listener applies. The tests then evaluate
// those selectors against the rail bUnit rendered, so a module that focuses the wrong element fails here,
// and the real-browser probe checks where focus lands.
internal static class LayersRailScript
{
    private const string Harness = """
        import { pathToFileURL } from 'node:url'
        const [file, id, keys] = process.argv.slice(1)
        const module = await import(pathToFileURL(file).href)
        let focusSelector = null
        module.focusNode({ querySelector: selector => { focusSelector = selector; return { focus() {} } }, contains: () => true }, id)
        let listener = null
        module.observeTreeKeys({ addEventListener: (type, handler) => { if (type === 'keydown') listener = handler } })
        const keyEvents = JSON.parse(keys).map(key => {
          const queried = []
          let prevented = false
          const target = { matches: selector => { queried.push(selector); return true }, closest: selector => { queried.push(selector); return target } }
          listener?.({ key, target, preventDefault: () => { prevented = true } })
          return { key, queried, prevented }
        })
        process.stdout.write(JSON.stringify({ focusSelector, keyEvents }))
        """;

    public sealed record KeyEvent(string Key, string[] Queried, bool Prevented);
    public sealed record Result(string? FocusSelector, KeyEvent[] KeyEvents);

    public static Result Run(string id, params string[] keys)
    {
        var start = new ProcessStartInfo("node") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(Harness);
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(Repo("projections/blazor/ui/hlp.ui.layers-rail/wwwroot/layers-rail.js"));
        start.ArgumentList.Add(id);
        start.ArgumentList.Add(JsonSerializer.Serialize(keys));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return JsonSerializer.Deserialize<Result>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    // The element the module would focus for a node id, found in the rendered rail.
    public static AngleSharp.Dom.IElement? FocusTarget(AngleSharp.Dom.IElement root, string id)
    {
        var selector = Run(id).FocusSelector;
        return selector is null ? null : root.QuerySelector(selector);
    }

    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);
}
