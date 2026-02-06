using System;
using System.Collections.Generic;

namespace Pos.Terminal.ViewModels;

public enum ExportTemplateKind
{
    Sales,
    Purchases,
    Customers,
    Inventory,
    LowStock,
    TopProducts,
    Profit
}

public sealed record ExportTemplateDefinition(string Name, string Description, ExportTemplateKind Kind);

public sealed class ExportRow
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public ExportRow(IReadOnlyDictionary<string, string> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public string? this[string columnName]
        => _values.TryGetValue(columnName, out var value) ? value : null;
}
