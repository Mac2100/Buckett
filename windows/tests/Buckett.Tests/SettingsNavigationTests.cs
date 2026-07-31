using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Buckett.Views;
using Xunit;

namespace Buckett.Tests;

/// Settings used to be a TabControl. Avalonia's default tab header is 24pt with
/// heavy padding, so six tabs wrapped onto two rows and ate a third of the
/// window before a single setting appeared. It is now a navigation rail, and
/// the rail is wired to the pages by name — a row tagged "Updates" shows the
/// control called "UpdatesPage". Nothing in the compiler enforces that, so
/// these tests do.
public class SettingsNavigationTests
{
    private static readonly string[] Sections =
    {
        "Accounts", "Appearance", "General", "Notifications", "Updates", "About"
    };

    [AvaloniaFact]
    public void EverySectionHasAPage()
    {
        var window = new SettingsWindow();

        foreach (var section in Sections)
        {
            Assert.NotNull(window.FindControl<Control>(section + "Page"));
        }
    }

    [AvaloniaFact]
    public void TheRailListsEverySectionInOrder()
    {
        var window = new SettingsWindow();
        var nav = window.FindControl<ListBox>("Nav")!;

        var tags = nav.Items.OfType<ListBoxItem>().Select(item => item.Tag as string);

        Assert.Equal(Sections, tags);
    }

    /// The failure this guards against is silent: a mistyped page name would
    /// simply show a blank pane rather than throwing.
    [AvaloniaFact]
    public void SelectingASectionShowsExactlyThatPage()
    {
        var window = new SettingsWindow();
        var nav = window.FindControl<ListBox>("Nav")!;

        for (var index = 0; index < Sections.Length; index++)
        {
            nav.SelectedIndex = index;

            var visible = Sections
                .Where(section => window.FindControl<Control>(section + "Page")!.IsVisible)
                .ToArray();

            Assert.Equal(new[] { Sections[index] }, visible);
        }
    }

    [AvaloniaFact]
    public void TheFirstSectionIsShownOnOpening()
    {
        var window = new SettingsWindow();

        Assert.True(window.FindControl<Control>("AccountsPage")!.IsVisible);
    }
}
