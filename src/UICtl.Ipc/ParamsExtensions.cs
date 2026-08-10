using System.Text.Json;
using UICtl.Core;

namespace UICtl.Ipc;

/// <summary>Typed accessors over the raw JSON params object every command receives, whether it arrived from the CLI or an MCP tool call.</summary>
internal static class ParamsExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string name) =>
        TryGetNonNull(element, name, out var v) ? v.GetString() : null;

    public static string GetStringOrThrow(this JsonElement element, string name) =>
        GetStringOrNull(element, name) ?? throw new UiCtlException($"\"{name}\" is required");

    public static bool GetBoolOrDefault(this JsonElement element, string name, bool defaultValue = false) =>
        TryGetNonNull(element, name, out var v) ? v.GetBoolean() : defaultValue;

    public static int? GetIntOrNull(this JsonElement element, string name) =>
        TryGetNonNull(element, name, out var v) ? v.GetInt32() : null;

    public static long? GetLongOrNull(this JsonElement element, string name) =>
        TryGetNonNull(element, name, out var v) ? v.GetInt64() : null;

    public static double? GetDoubleOrNull(this JsonElement element, string name) =>
        TryGetNonNull(element, name, out var v) ? v.GetDouble() : null;

    private static bool TryGetNonNull(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            return true;
        value = default;
        return false;
    }
}
