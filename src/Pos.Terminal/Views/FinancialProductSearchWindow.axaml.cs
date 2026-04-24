using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class FinancialProductSearchWindow : Window
{
    private readonly SearchState _state;

    public FinancialProductSearchWindow(IEnumerable<ProductChoice> products)
    {
        InitializeComponent();
        _state = new SearchState(products);
        DataContext = _state;
    }

    private void UseSelected_Click(object? sender, RoutedEventArgs e)
        => Close(_state.SelectedProduct?.Id);

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => Close(null);

    private sealed class SearchState : INotifyPropertyChanged
    {
        private readonly IReadOnlyList<ProductChoice> _products;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? string.Empty;
                Raise();
                Raise(nameof(FilteredProducts));
            }
        }

        private ProductChoice? _selectedProduct;
        public ProductChoice? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                _selectedProduct = value;
                Raise();
            }
        }

        public IEnumerable<ProductChoice> FilteredProducts => string.IsNullOrWhiteSpace(SearchText)
            ? _products
            : _products.Where(MatchesSearch);

        public SearchState(IEnumerable<ProductChoice> products)
        {
            _products = products.ToList();
        }

        private bool MatchesSearch(ProductChoice product)
        {
            var term = SearchText;
            if (string.IsNullOrWhiteSpace(term))
                return true;

            return ContainsIgnoreCase(product.Name, term) || ContainsIgnoreCase(product.Sku, term);
        }

        private static bool ContainsIgnoreCase(string? value, string term)
            => !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
            
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
