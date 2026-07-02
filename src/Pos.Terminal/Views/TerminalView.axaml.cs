using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Pos.Terminal.Models;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class TerminalView : UserControl
{
    private MainWindowViewModel? VM => DataContext as MainWindowViewModel;

    public TerminalView()
    {
        InitializeComponent();

        DataContextChanged += (_, __) => WireVm();
        this.AddHandler(KeyDownEvent, View_KeyDown, RoutingStrategies.Tunnel);
        this.AddHandler(InputElement.PointerPressedEvent, View_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void WireVm()
    {
        var vm = VM;
        if (vm == null) return;

        vm.EditQtyRequested = EditQtyForLineAsync;
        vm.ItemLookupRequested = SelectItemFromInventoryAsync;
    }

    public async void AddMisc_Click(object? sender, RoutedEventArgs e)
    {
        var vm = VM;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (vm == null || owner == null) return;

        var dialog = new MiscItemDialog();
        var result = await dialog.ShowDialog<MiscItemResult?>(owner);
        if (result != null)
            vm.AddMiscellaneousItem(result.Name, result.Description, result.Quantity, result.UnitPrice, result.VatInclusive);
    }

    private void View_KeyDown(object? sender, KeyEventArgs e)
    {
        var vm = VM;
        if (vm == null) return;

        if (e.Key == Key.F4)
        {
            vm.SelectCustomerFromTerminal();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            _ = PayFlowAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            vm.AddSearchItemCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (vm.SelectedCartLine != null)
                vm.RemoveLine(vm.SelectedCartLine);
            e.Handled = true;
        }
    }

    private async void View_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = VM;
        if (vm == null || !e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        var src = e.Source as Control;
        var row = FindAncestor<DataGridRow>(src);

        if (row?.DataContext is not CartLine line)
            return;

         vm.SelectedCartLine = line;

        var menu = new ContextMenu();

        var edit = new MenuItem { Header = "Edit Quantity" };
        var zeroRated = new MenuItem { Header = line.ZeroRated ? "✓ Zero Rated" : "Zero Rated" };
        var del = new MenuItem { Header = "Delete Item" };

        edit.Click += async (_, __) => await EditQtyForLineAsync(line);
        zeroRated.Click += (_, __) => line.ZeroRated = !line.ZeroRated;
        del.Click += (_, __) => vm.RemoveLine(line);

        menu.Items.Add(edit);
        menu.Items.Add(zeroRated);
        menu.Items.Add(del);

        menu.PlacementTarget = row;
        menu.Open(row);

        e.Handled = true;
    }

    private static T? FindAncestor<T>(Control? start) where T : class
    {
        Control? current = start;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.GetVisualParent() as Control;
        }

        return null;
    }

   private async Task<ProductDto?> SelectItemFromInventoryAsync()
    {
        var vm = VM;
        var host = GetHostWindow();
        if (vm == null || host == null) return null;

        var search = new TextBox { Watermark = "Search by item #, name, description, location", MinWidth = 360 };
        var list = new ListBox { Height = 380 };
        list.ItemsSource = vm.FindInventoryItems("").ToList();

        list.ItemTemplate = new FuncDataTemplate<ProductDto>((item, _) =>
           {
            if (item is null)
                return new TextBlock { Text = string.Empty };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("140,*,Auto"),
                Margin = new Thickness(4),
                [ToolTip.TipProperty] = string.IsNullOrWhiteSpace(item.Location) ? null : $"Location: {item.Location}"
            };
            row.Children.Add(new TextBlock { Text = item.Sku });
            row.Children.Add(new TextBlock { Text = item.Name, [Grid.ColumnProperty] = 1 });
            row.Children.Add(new TextBlock { Text = item.DisplayPrice, [Grid.ColumnProperty] = 2 });
            return row;
        });
        ProductDto? selected = null;

        void Refresh()
        {
              list.ItemsSource = vm.FindInventoryItems(search.Text ?? "").ToList();
        }
          var searchSubscription = search.GetObservable(TextBox.TextProperty).Subscribe(_ => Refresh());

        var ok = new Button { Content = "Add", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var win = new Window
        {
            Title = "Search Item",
            Width = 760,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    search,
                    list,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        list.DoubleTapped += (_, __) =>
        {
            if (list.SelectedItem is ProductDto p)
            {
                selected = p;
                win.Close();
            }
        };

        ok.Click += (_, __) =>
        {
            if (list.SelectedItem is ProductDto p)
            {
                selected = p;
                win.Close();
            }
        };

        cancel.Click += (_, __) => win.Close();

        try
        {
            await win.ShowDialog(host);
        }
        finally
        {
            searchSubscription.Dispose();
        }
        return selected;
    }

    public void SelectCustomer_Click(object? sender, RoutedEventArgs e)
        => VM?.SelectCustomerFromTerminal();

    public void ClearCustomer_Click(object? sender, RoutedEventArgs e)
        => VM?.ClearCustomer();    public void Clear_Click(object? sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm == null) return;

        vm.ClearCart();
        RefreshTotalsSafe();
    }

    public async void Discount_Click(object? sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm == null) return;

        if (vm.Subtotal <= 0m)
        {
            vm.Toast("Add items to cart before applying a discount.");
            return;
        }

        var currentPct = vm.Subtotal <= 0m
            ? 0m
            : Math.Round((vm.DiscountAmount / vm.Subtotal) * 100m, 0, MidpointRounding.AwayFromZero);

         var discountPct = await ShowDiscountPercentageInputAsync("Discount", "Select discount percentage:", currentPct, vm.Subtotal);

        if (discountPct == null) return;

        vm.DiscountAmount = Math.Round(vm.Subtotal * (discountPct.Value / 100m), 2, MidpointRounding.AwayFromZero);
        RefreshTotalsSafe();
    }

    public async void Pay_Click(object? sender, RoutedEventArgs e)
        => await PayFlowAsync();

    private async Task PayFlowAsync()
    {
        var vm = VM;
        if (vm == null) return;

        if (vm.CartLines.Count == 0)
        {
            vm.Toast("Cart is empty.");
            return;
        }

        var host = GetHostWindow();
        if (host == null) return;

        var dialog = new PayDialog(vm.TotalDue, vm.SelectedCustomerId != null);
        var ok = await dialog.ShowDialog<bool>(host);
        if (!ok) return;

        var r = dialog.Result;
        if (r.Method == PaymentMethod.None) return;

        if (r.Method == PaymentMethod.Cash)
        {
            if (r.CashTendered < vm.TotalDue)
            {
                vm.Toast("Insufficient cash.");
                return;
            }

            var totalDue = vm.TotalDue;
            await vm.CheckoutCashAsync(r.CashTendered);

            var change = Math.Round(r.CashTendered - totalDue, 2, MidpointRounding.AwayFromZero);
            await ShowMessageAsync($"Change due: ${change:0.00}");
            return;
        }

        if (r.Method == PaymentMethod.Debit)
        {
            await vm.CheckoutCardAsync("DEBIT");
            return;
        }

        if (r.Method == PaymentMethod.Credit)
        {
            await vm.CheckoutCardAsync("CREDIT");
            return;
        }

        if (r.Method == PaymentMethod.OnAccount)
        {
            if (vm.SelectedCustomerId == null)
            {
                await ShowMessageAsync("Select a customer to charge On Account.");
                vm.SelectCustomerFromTerminal();
                return;
            }

            await vm.CheckoutOnAccountAsync();
        }
    }

    public async void CashCheckout_Click(object? sender, RoutedEventArgs e)
    {
        var vm = VM;
        if (vm == null) return;

        if (vm.CartLines.Count == 0)
        {
            vm.Toast("Cart is empty.");
            return;
        }

        var totalDue = vm.TotalDue;

        var tendered = await ShowDecimalInputAsync(
            title: "Cash Payment",
            prompt: $"Total due is ${totalDue:0.00}. Enter amount tendered:",
            defaultValue: totalDue);

        if (tendered == null) return;

        if (tendered.Value < totalDue)
        {
            await ShowMessageAsync($"Insufficient cash. Total due is ${totalDue:0.00}");
            return;
        }

        await vm.CheckoutCashAsync(tendered.Value);

        var change = Math.Round(tendered.Value - totalDue, 2, MidpointRounding.AwayFromZero);
        await ShowMessageAsync($"Change due: ${change:0.00}");

    }

    private async Task EditQtyForLineAsync(CartLine line)
    {
        if (line.IsLength)
        {
            var inches = await ShowIntInputAsync("Edit Quantity", "Enter quantity in inches:", Math.Max(0, line.QtyInches));

            if (inches == null) return;
            line.QtyInches = Math.Max(0, inches.Value);
        }
        else
        {
            var qty = await ShowDecimalInputAsync("Edit Quantity", "Enter quantity:", Math.Max(0m, line.Qty));

            if (qty == null) return;
            line.Qty = Math.Max(0m, qty.Value);
        }

        RefreshTotalsSafe();
    }

    private void RefreshTotalsSafe()
    {
        var vm = VM;
        if (vm == null) return;

        try
        {
           var mi = vm.GetType().GetMethod("RefreshTotals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            mi?.Invoke(vm, null);
        }
        catch { }
    }

    private Window? GetHostWindow() => TopLevel.GetTopLevel(this) as Window;

    private async Task ShowMessageAsync(string message, string title = "Notice")
    {
        var host = GetHostWindow();
        if (host == null) return;

        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };

        var win = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, ok }
            }
        };

        ok.Click += (_, __) => win.Close();
        await win.ShowDialog(host);
    }

    private async Task<decimal?> ShowDecimalInputAsync(string title, string prompt, decimal defaultValue)
    {
        var host = GetHostWindow();
        if (host == null) return null;

        var box = new TextBox { Text = defaultValue.ToString("0.00", CultureInfo.InvariantCulture), MinWidth = 220 };

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        decimal? result = null;

        var win = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, __) =>
        {
            var raw = (box.Text ?? "").Trim().Replace("$", "");
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var val)) return;

            result = Math.Round(val, 2, MidpointRounding.AwayFromZero);
            win.Close();
        };

        cancel.Click += (_, __) => win.Close();
        await win.ShowDialog(host);
        return result;
    }

    private async Task<int?> ShowIntInputAsync(string title, string prompt, int defaultValue)
    {
        var host = GetHostWindow();
        if (host == null) return null;

        var box = new TextBox { Text = defaultValue.ToString(CultureInfo.InvariantCulture), MinWidth = 220 };

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        int? result = null;

        var win = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, __) =>
        {
            var raw = (box.Text ?? "").Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)) return;

            result = Math.Max(0, val);
            win.Close();
        };

        cancel.Click += (_, __) => win.Close();
        await win.ShowDialog(host);
        return result;
    }

     private async Task<decimal?> ShowDiscountPercentageInputAsync(string title, string prompt, decimal defaultValue, decimal subtotal)
    {
        var host = GetHostWindow();
        if (host == null) return null;

        var initialPct = Math.Clamp(Math.Round(defaultValue, 0, MidpointRounding.AwayFromZero), 0m, 99m);

         var slider = new Slider { Minimum = 0, Maximum = 99, Value = (double)initialPct, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 260 };
        var percentageText = new TextBlock { Text = $"{initialPct:0}%", FontSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var amountText = new TextBlock { Text = $"Discount amount: ${Math.Round(subtotal * (initialPct / 100m), 2, MidpointRounding.AwayFromZero):0.00}", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };

        slider.PropertyChanged += (_, args) =>
        {
           if (args.Property != Slider.ValueProperty) return;
            var pct = Math.Clamp(Math.Round((decimal)slider.Value, 0, MidpointRounding.AwayFromZero), 0m, 99m);
            percentageText.Text = $"{pct:0}%";
            amountText.Text = $"Discount amount: ${Math.Round(subtotal * (pct / 100m), 2, MidpointRounding.AwayFromZero):0.00}";
        };

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        decimal? result = null;

        var win = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    percentageText,
                    slider,
                    amountText,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, __) =>
        {
            result = Math.Clamp(Math.Round((decimal)slider.Value, 0, MidpointRounding.AwayFromZero), 0m, 99m);
            win.Close();
        };

        cancel.Click += (_, __) => win.Close();

        await win.ShowDialog(host);
        return result;
    }
}
