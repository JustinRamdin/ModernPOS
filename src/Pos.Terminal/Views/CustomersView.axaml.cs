using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class CustomersView : UserControl
{
    private ContextMenu? _customerMenu;

    public CustomersView()
    {
        InitializeComponent();
        CustomerList.AddHandler(PointerPressedEvent, CustomerList_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void CustomerList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        var source = e.Source as Control;
        var item = FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not CustomerRow customer)
            return;

        if (DataContext is CustomersViewModel vm)
            vm.Selected = customer;

        _customerMenu?.Close();

        var menu = new ContextMenu();
        var details = new MenuItem { Header = "View Details" };
        details.Click += async (_, _) => await OpenCustomerDetailsAsync(customer);
        menu.Items.Add(details);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_customerMenu, menu))
                _customerMenu = null;
        };
        menu.PlacementTarget = item;
        _customerMenu = menu;
        menu.Open(item);
        e.Handled = true;
    }

    private async Task OpenCustomerDetailsAsync(CustomerRow customer)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var detailsVm = new CustomerDetailsViewModel(customer.Id, customer.Name);
        var window = new CustomerDetailsWindow { DataContext = detailsVm };
        await detailsVm.LoadAsync();

        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();
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
}
