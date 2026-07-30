using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Buckett.Views;

public partial class MissingCredentialsView : UserControl
{
    public MissingCredentialsView() => InitializeComponent();

    private void OnOpenSettings(object? sender, RoutedEventArgs e) =>
        SettingsWindow.Present(TopLevel.GetTopLevel(this) as Window, "Accounts");
}
