using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pos.Terminal.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
