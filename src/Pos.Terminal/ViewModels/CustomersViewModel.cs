using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Contracts;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class CustomersViewModel : INotifyPropertyChanged
{
    private readonly Func<Guid?, Task>? _onPicked;
    private readonly VmRelayCommand _deleteCommand;
    private readonly VmRelayCommand _pickSelectedCommand;
    public bool IsPicker { get; set; }
    public bool IsNotPicker => !IsPicker;
    public bool IsHasSelection => Selected != null;
    public bool IsNoSelection => Selected == null;
    public bool ShowEditor => Selected != null || !IsPicker;
    public string EditorTitle => Selected == null ? "Customer Details" : "Customer Details";
    public string PickerHint => IsPicker ? "Pick a customer and press Select." : "";
    public string Title => IsPicker ? "Select Customer" : "Customers";
    public ObservableCollection<CustomerRow> Customers { get; } = new();
    private CustomerRow? _selected;
    public CustomerRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;

            _selected = value;
            Raise();
            Raise(nameof(IsHasSelection));
            Raise(nameof(IsNoSelection));
            Raise(nameof(ShowEditor));
            Raise(nameof(EditorTitle));

            _deleteCommand.NotifyCanExecuteChanged();
            _pickSelectedCommand.NotifyCanExecuteChanged();

            LoadSelectedToEditor();
        }
    }
    public string Search { get; set; } = "";
    public string ListStatus { get; set; } = "Ready";
    private Guid? _editId;

 public string EditName { get; set; } = "";
    public string EditPhone { get; set; } = "";
    public string EditEmail { get; set; } = "";
    public string EditArea { get; set; } = "";
    public decimal Balance { get; set; }
    public string BalanceLabel => $"Balance: {Balance:0.00}";
    public string PayAmount { get; set; } = "";
    public object? PayMethod { get; set; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ApplyPaymentCommand { get; }
    public ICommand ClearPaymentCommand { get; }

    public ICommand PickSelectedCommand { get; }
    public ICommand CancelPickCommand { get; }
    public CustomersViewModel(bool isPicker = false, Func<Guid?, Task>? onPicked = null)
    {
        IsPicker = isPicker; _onPicked = onPicked;
        NewCommand = new VmRelayCommand(_ => New(), _ => !IsPicker);
        SaveCommand = new VmRelayCommand(async _ => await SaveAsync(), _ => !IsPicker);
        _deleteCommand = new VmRelayCommand(async _ => await DeleteAsync(), _ => !IsPicker && Selected != null);
        DeleteCommand = _deleteCommand;
        ApplyPaymentCommand = new VmRelayCommand(async _ => await ApplyPaymentAsync(), _ => !IsPicker);
        ClearPaymentCommand = new VmRelayCommand(_ => ClearPayment(), _ => !IsPicker);
       _pickSelectedCommand = new VmRelayCommand(async _ => await PickSelectedAsync(), _ => IsPicker && Selected != null);
        PickSelectedCommand = _pickSelectedCommand;
        CancelPickCommand = new VmRelayCommand(async _ => await CancelPickAsync(), _ => IsPicker);
    }

    public async Task LoadAsync()
    {
        try
        {
            ListStatus = "Loading..."; Raise(nameof(ListStatus));
            using var api = await CreateApiAsync();
            var rows = await api.GetCustomersAsync();
            var filtered = string.IsNullOrWhiteSpace(Search) ? rows : rows.Where(c => c.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || c.Phone.Contains(Search, StringComparison.OrdinalIgnoreCase) || c.Email.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
            Customers.Clear(); foreach (var c in filtered) Customers.Add(new CustomerRow(c.Id, c.Name, c.Phone, c.Email, c.Area, c.Balance));
            ListStatus = Customers.Count == 0 ? "No customers on server." : $"{Customers.Count} customer(s)"; Raise(nameof(ListStatus));
        }
        catch (Exception ex) { ListStatus = $"Error: {ex.Message}"; Raise(nameof(ListStatus)); }
    }

    private void LoadSelectedToEditor()
    {
        if (Selected == null) { _editId = null; EditName = EditPhone = EditEmail = EditArea = ""; Balance = 0; NotifyEditor(); return; }
        _editId = Selected.Id; EditName = Selected.Name; EditPhone = Selected.Phone; EditEmail = Selected.Email; EditArea = Selected.Area; Balance = Selected.Balance; NotifyEditor();
    }

     private void New() { Selected = null; _editId = null; EditName = EditPhone = EditEmail = EditArea = PayAmount = ""; Balance = 0m; NotifyEditor(); Raise(nameof(PayAmount)); }

    private async Task SaveAsync()
    {
         using var api = await CreateApiAsync();
        var req = new UpsertCustomerRequest(EditName.Trim(), EditPhone.Trim(), EditEmail.Trim(), EditArea.Trim(), Balance, true);
        if (_editId is null) await api.CreateCustomerAsync(req); else await api.UpdateCustomerAsync(_editId.Value, req);
        await LoadAsync();
    }
     private async Task DeleteAsync() { if (Selected == null) return; using var api = await CreateApiAsync(); await api.DeleteCustomerAsync(Selected.Id); Selected = null; await LoadAsync(); }
    private async Task ApplyPaymentAsync() { if (Selected == null) return; if (!decimal.TryParse(PayAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt)) return; Balance = Math.Max(0m, Balance - amt); await SaveAsync(); ClearPayment(); }
    private void ClearPayment() { PayAmount = ""; PayMethod = null; }
    private async Task PickSelectedAsync() { if (_onPicked != null) await _onPicked(Selected?.Id); }
    private async Task CancelPickAsync() { if (_onPicked != null) await _onPicked(null); }
    private void NotifyEditor() { foreach (var n in new[]{ nameof(EditName), nameof(EditPhone), nameof(EditEmail), nameof(EditArea), nameof(Balance), nameof(BalanceLabel) }) Raise(n); }
    private static async Task<RemoteServerApi> CreateApiAsync() { var d = await new SettingsStore().LoadDeploymentAsync(); return new RemoteServerApi(d.ServerHost, d.ServerPort, d.AuthToken); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class CustomerRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));    public Guid Id { get; }

    private string _name; public string Name { get => _name; set { _name = value; Raise(); } }
    private string _phone; public string Phone { get => _phone; set { _phone = value; Raise(); } }
    private string _email; public string Email { get => _email; set { _email = value; Raise(); } }
    private string _area; public string Area { get => _area; set { _area = value; Raise(); } }
    private decimal _balance; public decimal Balance { get => _balance; set { _balance = value; Raise(); } }

    public string PhoneLine => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
    public string EmailLine => string.IsNullOrWhiteSpace(Email) ? "—" : Email;
    public string AreaLine => string.IsNullOrWhiteSpace(Area) ? "—" : Area;
public CustomerRow(Guid id, string name, string phone, string email, string area, decimal balance) { Id = id; _name = name; _phone = phone; _email = email; _area = area; _balance = balance; }
}
public sealed class VmRelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute; private readonly Func<object?, Task>? _executeAsync; private readonly Action<object?>? _execute;

    public event EventHandler? CanExecuteChanged;

    public VmRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public VmRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null) { _executeAsync = executeAsync; _canExecute = canExecute; }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter) { if (_execute != null) _execute(parameter); else if (_executeAsync != null) await _executeAsync(parameter); }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
