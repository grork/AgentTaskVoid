using Atv.Icons;
using Atv.LogicTests.Persistence;
using Atv.LogicTests.Store;
using Atv.Operations;
using Atv.Persistence;

namespace Atv.LogicTests.Operations;

/// <summary>
/// Phase-07's wiring obligation: attach icon cleanup to the cleanup paths
/// phases 04/05 already built icon-unaware. Deliberately NOT built on the
/// shared <see cref="OperationsHarness"/> (which every other Operations test
/// in this project depends on staying icon-UNAWARE-compatible, i.e.
/// constructible with no <see cref="IconService"/> at all) -- a small local
/// rig here instead, wiring a real <see cref="IconService"/> alongside the
/// same temp-dir <see cref="SidecarStore"/>/<see cref="RecycleBin"/> pattern.
/// </summary>
[TestClass]
public sealed class TaskOperationsIconTests
{
    private static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig : IDisposable
    {
        private readonly TempDirectory _iconsRoot = new();
        private readonly TempDirectory _sidecarDir = new();
        private readonly TempDirectory _recycleDir = new();
        private readonly Mutex _mutex = new(initiallyOwned: false);

        public FakeAppTaskStore Store { get; } = new();
        public SidecarStore Sidecar { get; }
        public RecycleBin RecycleBin { get; }
        public IconService Icons { get; }
        public TaskOperations Ops { get; }

        public Rig()
        {
            Sidecar = new SidecarStore(_sidecarDir.Path);
            RecycleBin = new RecycleBin(_recycleDir.Path);
            Icons = new IconService(_iconsRoot.Path, _recycleDir.Path);
            var gate = new WriteGate(_mutex);
            Ops = new TaskOperations(Store, Sidecar, RecycleBin, gate, Ttl, icons: Icons);
        }

        public void Dispose()
        {
            _mutex.Dispose();
            _iconsRoot.Dispose();
            _sidecarDir.Dispose();
            _recycleDir.Dispose();
        }
    }

    [TestMethod]
    public void Remove_ReapsTheLiveIconCopy()
    {
        using var rig = new Rig();
        Uri iconUri = rig.Icons.Place("h1", IconTokens.Default);
        rig.Ops.Start("h1", "T", "S", iconUri, DeepLink, Now);
        Assert.IsTrue(File.Exists(iconUri.LocalPath));

        rig.Ops.Remove("h1", Now);

        Assert.IsFalse(File.Exists(iconUri.LocalPath), "remove must reap the per-handle icon copy (ERGO-23).");
    }

    [TestMethod]
    public void ReconciliationDrop_StaleEntry_ReapsTheLiveIconCopy()
    {
        using var rig = new Rig();
        Uri iconUri = rig.Icons.Place("h1", IconTokens.Default);
        var started = rig.Ops.Start("h1", "T", "S", iconUri, DeepLink, Now);
        rig.Store.SimulateVanish(started.View!.Id); // the API forgot about it -- h1's sidecar mapping is now stale

        // Any start/remove triggers a full ReconcileAll pass, which will find h1's
        // stale mapping (rule 2) regardless of which handle THIS call targets.
        rig.Ops.Start("h2", "T2", "S2", rig.Icons.Place("h2", IconTokens.Default), DeepLink, Now);

        Assert.IsFalse(File.Exists(iconUri.LocalPath), "a reconciliation-dropped stale entry's icon must be reaped too.");
        Assert.IsNull(rig.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void ReconciliationDrop_HiddenSweep_ReapsTheLiveIconCopy()
    {
        using var rig = new Rig();
        Uri iconUri = rig.Icons.Place("h1", IconTokens.Default);
        var started = rig.Ops.Start("h1", "T", "S", iconUri, DeepLink, Now);
        rig.Store.SetHiddenByUser(started.View!.Id, true);

        rig.Ops.Start("h2", "T2", "S2", rig.Icons.Place("h2", IconTokens.Default), DeepLink, Now);

        Assert.IsFalse(File.Exists(iconUri.LocalPath), "the ERGO-2 hidden sweep's Remove()+drop must reap the icon too.");
        Assert.IsNull(rig.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void UpdateClassVerb_PerHandleStaleDrop_ReapsTheLiveIconCopy()
    {
        using var rig = new Rig();
        Uri iconUri = rig.Icons.Place("h1", IconTokens.Default);
        var started = rig.Ops.Start("h1", "T", "S", iconUri, DeepLink, Now);
        rig.Store.SimulateVanish(started.View!.Id);

        var outcome = rig.Ops.Step("h1", "irrelevant", Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind, "no recycle-bin record either, so this is a clean miss after the drop");
        Assert.IsFalse(File.Exists(iconUri.LocalPath), "the per-handle ResolveHandle rule-2 drop must reap the icon too, not just the full-pass ReconcileAll path.");
    }

    [TestMethod]
    public void Resurrection_MovesTheIconBackFromRecycleToTheLivePath()
    {
        using var rig = new Rig();
        Uri liveUri = rig.Icons.Place("h1", IconTokens.Default);
        byte[] originalBytes = File.ReadAllBytes(liveUri.LocalPath);
        rig.Icons.MoveToRecycle("h1"); // stands in for the not-yet-built watchdog's expiry-tombstone icon move
        rig.RecycleBin.Tombstone(new RecycleRecord("h1", "Stored Title", "Stored Subtitle", null, DeepLink, Now.AddHours(-1)));

        var outcome = rig.Ops.Step("h1", "first step after resurrection", Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(liveUri, outcome.View!.IconUri, "the resurrected card's icon must be the REAL moved-back file, not a placeholder.");
        Assert.IsTrue(File.Exists(liveUri.LocalPath));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(liveUri.LocalPath));

        Assert.IsNull(rig.RecycleBin.TryResurrect("h1", Now, Ttl), "tombstone consumed");
    }
}
