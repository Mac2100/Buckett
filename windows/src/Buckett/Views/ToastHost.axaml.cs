using Avalonia.Controls;
using Buckett.Services;

namespace Buckett.Views;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
        Host.ItemsSource = ToastCenter.Shared.Toasts;
    }
}
