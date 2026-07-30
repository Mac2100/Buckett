using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Buckett.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

/// Persists non-secret account configuration as JSON in the app's data folder.
/// Secrets go to the Windows Credential Manager (see `CredentialStore.cs`).
public sealed class AccountStore : ObservableObject
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public ObservableCollection<Account> Accounts { get; } = new();

    private string FilePath => Path.Combine(AppPaths.SupportDirectory, "accounts.json");

    public AccountStore()
    {
        Load();
    }

    /// On-disk shape — matches the macOS build's accounts.json exactly, so the
    /// same file works on either platform.
    private sealed class StoredAccount
    {
        public string id { get; set; } = Guid.NewGuid().ToString("D");
        public string name { get; set; } = "";
        public string provider { get; set; } = "r2";
        public string cloudflareAccountID { get; set; } = "";
        public string b2Region { get; set; } = "";
        public string customEndpoint { get; set; } = "";
        public string accessKeyID { get; set; } = "";
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var decoded = JsonSerializer.Deserialize<List<StoredAccount>>(json, SerializerOptions);
            if (decoded == null) return;

            Accounts.Clear();
            foreach (var stored in decoded)
            {
                Accounts.Add(new Account
                {
                    Id = Guid.TryParse(stored.id, out var id) ? id : Guid.NewGuid(),
                    Name = stored.name ?? "",
                    Provider = ProviderExtensions.FromRawValue(stored.provider),
                    CloudflareAccountID = stored.cloudflareAccountID ?? "",
                    B2Region = stored.b2Region ?? "",
                    CustomEndpoint = stored.customEndpoint ?? "",
                    AccessKeyID = stored.accessKeyID ?? ""
                });
            }
            OnPropertyChanged(nameof(Accounts));
        }
        catch (Exception error)
        {
            Log.Warn($"failed to load accounts: {error.Message}");
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureSupportDirectory();
            var stored = Accounts.Select(account => new StoredAccount
            {
                id = account.Id.ToString("D").ToUpperInvariant(),
                name = account.Name,
                provider = account.Provider.RawValue(),
                cloudflareAccountID = account.CloudflareAccountID,
                b2Region = account.B2Region,
                customEndpoint = account.CustomEndpoint,
                accessKeyID = account.AccessKeyID
            }).ToList();

            var json = JsonSerializer.Serialize(stored, SerializerOptions);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Warn($"failed to save accounts: {error.Message}");
        }
    }

    public void Upsert(Account account, string? secretKey)
    {
        var index = -1;
        for (var i = 0; i < Accounts.Count; i++)
        {
            if (Accounts[i].Id == account.Id) { index = i; break; }
        }
        if (index >= 0)
        {
            Accounts[index] = account;
        }
        else
        {
            Accounts.Add(account);
        }
        if (!string.IsNullOrEmpty(secretKey))
        {
            CredentialStore.SetSecret(secretKey!, account.Id);
        }
        Save();
        OnPropertyChanged(nameof(Accounts));
    }

    public void Remove(Account account)
    {
        var existing = Accounts.FirstOrDefault(candidate => candidate.Id == account.Id);
        if (existing != null) Accounts.Remove(existing);
        CredentialStore.DeleteSecret(account.Id);
        Save();
        OnPropertyChanged(nameof(Accounts));
    }

    public string? SecretKey(Account account) => CredentialStore.Secret(account.Id);

    public S3Client? Client(Account account)
    {
        var secret = SecretKey(account);
        return secret == null ? null : S3Client.Create(account, secret);
    }
}
