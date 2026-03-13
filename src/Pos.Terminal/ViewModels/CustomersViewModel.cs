// File: src/Pos.Terminal/ViewModels/CustomersViewModel.cs
// Replace the ENTIRE file with this (copy/paste).
//
// ✅ Customers now SAVE to the SAME DB: pos.local.db
// ✅ No more demo/in-memory customers
// ✅ Works as normal screen OR picker mode

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using DataLocalDb = Pos.Local.Data.LocalDb;

using Pos.Local.Data;
using Pos.Local.Entities;
using Pos.Local.Services;
namespace Pos.Terminal.ViewModels;

public sealed class CustomersViewModel : INotifyPropertyChanged
{
    // -------------------------
    // Picker callback + mode
    // -------------------------
    private readonly Func<Guid?, Task>? _onPicked;

    private bool _isPicker;
    public bool IsPicker
    {
        get => _isPicker;
        set
        {
            _isPicker = value;
            Raise();
            Raise(nameof(IsNotPicker));
            Raise(nameof(PickerHint));
            Raise(nameof(Title));
        }

    }
    
    public bool IsNotPicker => !IsPicker;
    public bool IsHasSelection => Selected != null;
    public bool IsNoSelection => Selected == null;

    public string PickerHint => IsPicker ? "Pick a customer and press Select." : "";
    public string Title => IsPicker ? "Select Customer" : "Customers";

    // -------------------------
    // Data
    // -------------------------
    public ObservableCollection<CustomerRow> Customers { get; } = new();
    
    private CustomerRow? _selected;
    public CustomerRow? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Raise();
            Raise(nameof(IsHasSelection));
            Raise(nameof(IsNoSelection));
            LoadSelectedToEditor();

            (PickSelectedCommand as VmRelayCommand)?.NotifyCanExecuteChanged();
            (DeleteCommand as VmRelayCommand)?.NotifyCanExecuteChanged();
            (SaveCommand as VmRelayCommand)?.NotifyCanExecuteChanged();
            (ApplyPaymentCommand as VmRelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            _search = value ?? "";
            Raise();
            _ = LoadAsync();
        }
    }

    private string _listStatus = "Ready";
    public string ListStatus
    {
        get => _listStatus;
        set { _listStatus = value; Raise(); }
    }

    // -------------------------
    // Editor fields
    // -------------------------
    private Guid? _editId;

    private string _editName = "";
    public string EditName
    {
        get => _editName;
        set { _editName = value ?? ""; Raise(); (SaveCommand as VmRelayCommand)?.NotifyCanExecuteChanged(); }
    }

    private string _editPhone = "";
    public string EditPhone
    {
        get => _editPhone;
        set { _editPhone = value ?? ""; Raise(); (SaveCommand as VmRelayCommand)?.NotifyCanExecuteChanged(); }
    }

    private string _editEmail = "";
    public string EditEmail
    {
        get => _editEmail;
        set { _editEmail = value ?? ""; Raise(); (SaveCommand as VmRelayCommand)?.NotifyCanExecuteChanged(); }
    }

    private string _editArea = "";
    public string EditArea
    {
        get => _editArea;
        set { _editArea = value ?? ""; Raise(); (SaveCommand as VmRelayCommand)?.NotifyCanExecuteChanged(); }
    }

    private decimal _balance;
    public decimal Balance
    {
        get => _balance;
        set { _balance = value; Raise(); Raise(nameof(BalanceLabel)); }
    }

    public string BalanceLabel => $"Balance: {Balance:0.00}";

    // Payment entry
    private string _payAmount = "";
    public string PayAmount
    {
        get => _payAmount;
        set { _payAmount = value ?? ""; Raise(); (ApplyPaymentCommand as VmRelayCommand)?.NotifyCanExecuteChanged(); }
    }

    private object? _payMethod;
    public object? PayMethod
    {
        get => _payMethod;
        set { _payMethod = value; Raise(); }
    }

    // -------------------------
    // Commands
    // -------------------------
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public ICommand ApplyPaymentCommand { get; }
    public ICommand ClearPaymentCommand { get; }

    public ICommand PickSelectedCommand { get; }
    public ICommand CancelPickCommand { get; }

    public CustomersViewModel(bool isPicker = false, Func<Guid?, Task>? onPicked = null)
    {
        IsPicker = isPicker;
        _onPicked = onPicked;

        NewCommand = new VmRelayCommand(_ => New(), _ => !IsPicker);
        SaveCommand = new VmRelayCommand(async _ => await SaveAsync(), _ => !IsPicker && CanSave());
        DeleteCommand = new VmRelayCommand(async _ => await DeleteAsync(), _ => !IsPicker && Selected != null);

        ApplyPaymentCommand = new VmRelayCommand(async _ => await ApplyPaymentAsync(), _ => !IsPicker && CanApplyPayment());
        ClearPaymentCommand = new VmRelayCommand(_ => ClearPayment(), _ => !IsPicker);

        PickSelectedCommand = new VmRelayCommand(async _ => await PickSelectedAsync(), _ => IsPicker && Selected != null);
        CancelPickCommand = new VmRelayCommand(async _ => await CancelPickAsync(), _ => IsPicker);

        ListStatus = "Ready";
    }

    // -------------------------
    // Load from SAME DB
    // -------------------------
    public async Task LoadAsync()
    {
        try
        {
            ListStatus = "Loading...";
            await using var db = new PosLocalDbContext(BuildDbOptions());
            await db.Database.EnsureCreatedAsync();
            await EnsureCustomerAreaColumnAsync(db);

            var s = (Search ?? "").Trim();

            IQueryable<Customer> q = db.Customers.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(s))
            {
                q = q.Where(c =>
                    c.Name.Contains(s) ||
                    c.Phone.Contains(s) ||
                    c.Email.Contains(s));
            }

            var rows = await q
                .OrderBy(c => c.Name)
                .Select(c => new CustomerRow(c.Id, c.Name, c.Phone, c.Email, c.Area, c.Balance))
                .ToListAsync();

            Customers.Clear();
            foreach (var r in rows) Customers.Add(r);

            ListStatus = Customers.Count == 0 ? "No customers" : $"{Customers.Count} customer(s)";
        }
        catch (Exception ex)
        {
            ListStatus = $"Error: {ex.Message}";
        }
    }

    // -------------------------
    // Editor load/save
    // -------------------------
    private void LoadSelectedToEditor()
    {
        if (Selected == null)
        {
            _editId = null;
            EditName = "";
            EditPhone = "";
            EditEmail = "";
            EditArea = "";
            Balance = 0m;
            return;
        }

        _editId = Selected.Id;
        EditName = Selected.Name;
        EditPhone = Selected.Phone;
        EditEmail = Selected.Email;
        EditArea = Selected.Area;
        Balance = Selected.Balance;
    }

    private void New()
    {
        Selected = null;
        _editId = null;
        EditName = "";
        EditPhone = "";
        EditEmail = "";
        EditArea = "";
        Balance = 0m;
        ClearPayment();
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(EditName)
        && !string.IsNullOrWhiteSpace(EditPhone)
        && !string.IsNullOrWhiteSpace(EditEmail)
        && !string.IsNullOrWhiteSpace(EditArea);

    private async Task SaveAsync()
    {
        if (!CanSave()) return;

        await using var db = new PosLocalDbContext(BuildDbOptions());
        await db.Database.EnsureCreatedAsync();
        await EnsureCustomerAreaColumnAsync(db);

        if (_editId == null)
        {
            var entity = new Customer
            {
                Id = Guid.NewGuid(),
                Name = EditName.Trim(),
                Phone = (EditPhone ?? "").Trim(),
                Email = (EditEmail ?? "").Trim(),
                Area = (EditArea ?? "").Trim(),
                Balance = Balance,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            db.Customers.Add(entity);
            await db.SaveChangesAsync();

            await LoadAsync();
            Selected = Customers.FirstOrDefault(x => x.Id == entity.Id);
        }
        else
        {
            var entity = await db.Customers.FirstOrDefaultAsync(x => x.Id == _editId.Value);
            if (entity == null) { await LoadAsync(); return; }

            entity.Name = EditName.Trim();
            entity.Phone = (EditPhone ?? "").Trim();
            entity.Email = (EditEmail ?? "").Trim();
            entity.Area = (EditArea ?? "").Trim();
            entity.Balance = Balance;
            entity.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await LoadAsync();
            Selected = Customers.FirstOrDefault(x => x.Id == entity.Id);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected == null) return;

        var id = Selected.Id;

        await using var db = new PosLocalDbContext(BuildDbOptions());
        await db.Database.EnsureCreatedAsync();
        await EnsureCustomerAreaColumnAsync(db);

        var entity = await db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (entity != null)
        {
            db.Customers.Remove(entity);
            await db.SaveChangesAsync();
        }

        Selected = null;
        await LoadAsync();
    }

    // -------------------------
    // Payment
    // -------------------------
    private bool CanApplyPayment()
    {
        if (Selected == null) return false;
        return TryParseMoney(PayAmount, out var amt) && amt > 0m;
    }

    private async Task ApplyPaymentAsync()
    {
        if (Selected == null) return;
        if (!TryParseMoney(PayAmount, out var amt)) return;
        if (amt <= 0m) return;

        var id = Selected.Id;

        await using var db = new PosLocalDbContext(BuildDbOptions());
        await db.Database.EnsureCreatedAsync();
        await EnsureCustomerAreaColumnAsync(db);

        var entity = await db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return;

        entity.Balance = Math.Max(0m, entity.Balance - amt);
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        ClearPayment();
        await LoadAsync();

        Selected = Customers.FirstOrDefault(x => x.Id == id);
        if (Selected != null) Balance = Selected.Balance;
    }

    private void ClearPayment()
    {
        PayAmount = "";
        PayMethod = null;
    }

    private static bool TryParseMoney(string? text, out decimal value)
    {
        text = (text ?? "").Trim();

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    // -------------------------
    // Picker actions
    // -------------------------
    private async Task PickSelectedAsync()
    {
        if (!IsPicker) return;
        if (_onPicked == null) return;

        await _onPicked(Selected?.Id);
    }

    private async Task CancelPickAsync()
    {
        if (!IsPicker) return;
        if (_onPicked == null) return;

        await _onPicked(null);
    }

     private static async Task EnsureCustomerAreaColumnAsync(PosLocalDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN Area TEXT NOT NULL DEFAULT '';");
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists.
        }
    }

    // ✅ SAME DB OPTIONS as Terminal + Inventory
    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
    => DataLocalDb.BuildOptions();

    // -------------------------
    // INotifyPropertyChanged
    // -------------------------
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// -------------------------
// Row model
// -------------------------
public sealed class CustomerRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; }

    private string _name;
    public string Name { get => _name; set { _name = value; Raise(); Raise(nameof(PhoneLine)); Raise(nameof(EmailLine)); } }

    private string _phone;
    public string Phone { get => _phone; set { _phone = value; Raise(); Raise(nameof(PhoneLine)); } }

    private string _email;
    public string Email { get => _email; set { _email = value; Raise(); Raise(nameof(EmailLine)); } }


    private string _area;
    public string Area { get => _area; set { _area = value; Raise(); Raise(nameof(AreaLine)); } }

    private decimal _balance;
    public decimal Balance { get => _balance; set { _balance = value; Raise(); } }

    public string PhoneLine => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
    public string EmailLine => string.IsNullOrWhiteSpace(Email) ? "—" : Email;
    public string AreaLine => string.IsNullOrWhiteSpace(Area) ? "—" : Area;

    public CustomerRow(Guid id, string name, string phone, string email, decimal balance)
    {
        Id = id;
        _name = name;
        _phone = phone;
        _email = email;
        _area = area;
        _balance = balance;
    }
}

// -------------------------
// Simple VM command (sync + async)
// -------------------------
public sealed class VmRelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute;
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Action<object?>? _execute;

    public event EventHandler? CanExecuteChanged;

    public VmRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public VmRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_execute != null) { _execute(parameter); return; }
        if (_executeAsync != null) await _executeAsync(parameter);
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
