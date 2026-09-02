using System.Text.RegularExpressions;

namespace Pos.Terminal.Services;

public static class InventoryNameHelper
{
    public static string BuildEasyName(string originalName, string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return originalName;

        var cleaned = Regex.Replace(description.Trim(), @"\s+", " ");
        var firstUsefulPart = cleaned
            .Split(['.', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.Length >= 3) ?? cleaned;

        if (firstUsefulPart.Length > 64)
            firstUsefulPart = firstUsefulPart[..64].TrimEnd(',', '-', '/', '\\', ' ');

        return string.IsNullOrWhiteSpace(firstUsefulPart) ? originalName : firstUsefulPart;
    }
}
