using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Buckett.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();

    private void OnAddAccount(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) OnboardingWindow.Present(owner);
    }
}
