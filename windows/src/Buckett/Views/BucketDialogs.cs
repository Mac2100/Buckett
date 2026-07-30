using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

public static class NewBucketWindow
{
    private static readonly Regex ValidName =
        new("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.Compiled);

    public static void Show(Window owner)
    {
        var state = AppState.Shared;
        var window = DialogChrome.Create("New Bucket", 420);

        var field = DialogChrome.Field("bucket-name");
        var rules = new TextBlock
        {
            Text = "3–63 characters; lowercase letters, numbers, and hyphens; " +
                   "must start and end with a letter or number.",
            Classes = { "caption", "secondary" },
            TextWrapping = TextWrapping.Wrap
        };
        var regionNote = new TextBlock
        {
            Classes = { "caption", "tertiary" },
            TextWrapping = TextWrapping.Wrap,
            Text = state.SelectedAccount is { } account
                ? account.Provider == Provider.CloudflareR2
                    ? "Region: automatic — R2 places the bucket close to where you create it."
                    : $"Created in your account region ({account.SigningRegion})."
                : ""
        };
        var errorLabel = new TextBlock
        {
            Classes = { "caption", "danger" },
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var create = DialogChrome.Prominent("Create");
        create.IsEnabled = false;
        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        string Trimmed() => (field.Text ?? "").Trim();

        field.TextChanged += (_, _) =>
        {
            var value = Trimmed();
            var valid = ValidName.IsMatch(value);
            create.IsEnabled = valid;
            rules.Classes.Set("warning", value.Length > 0 && !valid);
            rules.Classes.Set("secondary", value.Length == 0 || valid);
        };

        create.Click += async (_, _) =>
        {
            var name = Trimmed();
            create.IsEnabled = false;
            errorLabel.IsVisible = false;
            try
            {
                await state.CreateBucketAsync(name);
                window.Close();
                ToastCenter.Shared.Show("Bucket created", name);
                state.SidebarSelection = SidebarSelection.ForBucket(name);
            }
            catch (Exception error)
            {
                errorLabel.Text = error.Message;
                errorLabel.IsVisible = true;
                create.IsEnabled = true;
            }
        };

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("tray.full.fill", 30),
            DialogChrome.Title("New Bucket"),
            DialogChrome.Caption(state.SelectedAccount is { } selected
                ? $"In {selected.DisplayLabel}"
                : ""),
            field,
            rules,
            regionNote,
            errorLabel,
            DialogChrome.ButtonRow(null, cancel, create));

        window.Opened += (_, _) => field.Focus();
        _ = window.ShowDialog(owner);
    }
}

/// Type-to-confirm destructive flow: the bucket name must be typed exactly,
/// and emptying a non-empty bucket is an explicit opt-in. Emptying purges all
/// object versions and unfinished uploads (the hidden content that makes
/// "empty-looking" buckets undeletable on B2).
public static class DeleteBucketWindow
{
    public static void Show(Window owner, Account account, Bucket bucket)
    {
        var state = AppState.Shared;
        var window = DialogChrome.Create("Delete Bucket", 440);
        var stats = state.Stats(account.Id, bucket.Name);

        var contentsLine = new TextBlock
        {
            Classes = { "callout", "secondary" },
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = stats is { ObjectCount: > 0 },
            Text = stats is { ObjectCount: > 0 }
                ? $"It currently contains {stats.ObjectCount} " +
                  $"object{(stats.ObjectCount == 1 ? "" : "s")} ({stats.FormattedSize})."
                : ""
        };

        var emptyFirst = new CheckBox { Content = "Also delete all objects inside" };
        var emptyNote = new TextBlock
        {
            Text = "Purges every object, hidden file version, and unfinished upload. " +
                   "Required unless the bucket is truly empty.",
            Classes = { "caption", "secondary" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(28, 0, 0, 0)
        };

        var confirmField = DialogChrome.Field(bucket.Name);
        var errorLabel = new TextBlock
        {
            Classes = { "caption", "danger" },
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var delete = DialogChrome.Destructive("Delete Bucket");
        delete.MinWidth = 130;
        delete.IsEnabled = false;
        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        confirmField.TextChanged += (_, _) =>
            delete.IsEnabled = (confirmField.Text ?? "") == bucket.Name;

        delete.Click += async (_, _) =>
        {
            delete.IsEnabled = false;
            cancel.IsEnabled = false;
            delete.Content = emptyFirst.IsChecked == true ? "Emptying & deleting…" : "Deleting…";
            errorLabel.IsVisible = false;
            try
            {
                await state.DeleteBucketAsync(bucket.Name, account, emptyFirst.IsChecked == true);
                window.Close();
                ToastCenter.Shared.Show("Bucket deleted", bucket.Name);
            }
            catch (S3Exception error) when (error.Code == "BucketNotEmpty")
            {
                errorLabel.Text =
                    "The bucket still contains hidden file versions or unfinished uploads. " +
                    "Enable “Also delete all objects inside” to purge them first.";
                errorLabel.IsVisible = true;
                delete.Content = "Delete Bucket";
                delete.IsEnabled = true;
                cancel.IsEnabled = true;
            }
            catch (Exception error)
            {
                errorLabel.Text = error.Message;
                errorLabel.IsVisible = true;
                delete.Content = "Delete Bucket";
                delete.IsEnabled = true;
                cancel.IsEnabled = true;
            }
        };

        var warning = new TextBlock
        {
            Text = "This cannot be undone.",
            Classes = { "callout", "danger" },
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("trash.circle.fill", 34, "DangerBrush"),
            DialogChrome.Title("Delete Bucket"),
            new TextBlock
            {
                Text = $"This permanently deletes “{bucket.Name}” from " +
                       $"{account.Provider.DisplayName()}.",
                Classes = { "callout" },
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            },
            contentsLine,
            warning,
            emptyFirst,
            emptyNote,
            new TextBlock
            {
                Text = "Type the bucket name to confirm:",
                Classes = { "caption", "secondary" }
            },
            confirmField,
            errorLabel,
            DialogChrome.ButtonRow(null, cancel, delete));

        window.Opened += (_, _) => confirmField.Focus();
        _ = window.ShowDialog(owner);
    }
}
