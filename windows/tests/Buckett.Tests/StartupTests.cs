using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Buckett.Services;
using Buckett.ViewModels;
using Buckett.Views;
using Xunit;

namespace Buckett.Tests;

/// Buckett is a WinExe with no console, so anything that throws while the app
/// is starting kills it silently — the user just sees nothing happen. These
/// tests run that hazardous work up front, where a failure is visible.
///
/// This exists because it happened: SelfUpdater declared
///   static readonly HttpClient Http = new() { Timeout = Timeout };
///   static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
/// Static initialisers run in declaration order, so Http read TimeSpan.Zero,
/// HttpClient rejected it, and the type initialiser threw the moment the main
/// window's update overlay touched SelfUpdater.Shared.
public class StartupTests
{
    /// Types whose static initialiser must work off the UI thread, because
    /// background work reaches them. Types that build Avalonia objects are
    /// deliberately absent — brushes and controls verify dispatcher access, so
    /// they belong in the Avalonia tests instead. (Forcing one of those here
    /// would also poison it: .NET caches a failed type initialiser for the
    /// lifetime of the process.)
    public static IEnumerable<object[]> BackgroundSafeTypes() =>
        new[]
        {
            typeof(Settings),
            typeof(AppPaths),
            typeof(Log),
            typeof(SelfUpdater),
            typeof(UpdateChecker),
            typeof(ToastCenter),
            typeof(Notifier),
            typeof(BucketAliases),
            typeof(UploadHistory),
            typeof(ThumbnailLoader),
            typeof(AppState),
            typeof(Icons),
            typeof(NaturalComparer)
        }.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(BackgroundSafeTypes))]
    public void StaticInitialisersDoNotThrow(Type type)
    {
        var failure = Record.Exception(
            () => RuntimeHelpers.RunClassConstructor(type.TypeHandle));

        Assert.True(
            failure == null,
            $"The static initialiser for {type.Name} threw, which would kill the app " +
            $"at startup with no visible error:{Environment.NewLine}{failure}");
    }

    /// The singletons the startup path reaches for, resolved the same way the
    /// app resolves them.
    [Fact]
    public void SingletonsResolve()
    {
        Assert.NotNull(Settings.Shared);
        Assert.NotNull(SelfUpdater.Shared);
        Assert.NotNull(ToastCenter.Shared);
        Assert.NotNull(Notifier.Shared);
        Assert.NotNull(BucketAliases.Shared);
        Assert.NotNull(UploadHistory.Shared);
        Assert.NotNull(ThumbnailLoader.Shared);
        Assert.NotNull(AppState.Shared);
        Assert.NotNull(AppState.Shared.Transfers);
        Assert.NotNull(AppState.Shared.Updates);
        Assert.NotNull(AppState.Shared.AccountStore);
    }

    /// The updater's HTTP client has to be usable, not merely constructed —
    /// the original bug produced a type that could never be touched at all.
    [Fact]
    public void SelfUpdaterStartsIdle()
    {
        Assert.Equal(UpdatePhase.Idle, SelfUpdater.Shared.Phase);
        Assert.False(SelfUpdater.Shared.IsBusy);
    }
}
