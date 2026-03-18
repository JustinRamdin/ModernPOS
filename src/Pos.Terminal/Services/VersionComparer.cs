namespace Pos.Terminal.Services;

public static class VersionComparer
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Version.TryParse(Normalize(value), out version);
    }

    public static int Compare(string? left, string? right)
    {
        if (!TryParse(left, out var leftVersion))
            throw new InvalidOperationException($"Invalid version '{left}'.");

        if (!TryParse(right, out var rightVersion))
            throw new InvalidOperationException($"Invalid version '{right}'.");

        return leftVersion.CompareTo(rightVersion);
    }

    public static bool IsInRange(string? value, string? minInclusive, string? maxInclusive)
    {
        if (!TryParse(value, out var current))
            return false;

        if (TryParse(minInclusive, out var minVersion) && current < minVersion)
            return false;

        if (TryParse(maxInclusive, out var maxVersion) && current > maxVersion)
            return false;

        return true;
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length >= 3)
            return trimmed;

        return string.Join('.', segments.Concat(Enumerable.Repeat("0", 3 - segments.Length)));
    }
}
