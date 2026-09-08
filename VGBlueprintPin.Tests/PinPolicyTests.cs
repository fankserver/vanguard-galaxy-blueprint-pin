using System;
using VGModAPI;
using VGBlueprintPin.State;
using Xunit;

namespace VGBlueprintPin.Tests;

public sealed class PinPolicyTests
{
    private readonly RecipeId _recipe = new("vanilla", "forge/test");
    private readonly RecipeStationHandle _station = new(Guid.NewGuid(), Guid.NewGuid(), "Station");
    private CraftingJobSnapshot Job(CraftingJobHandle handle, int remaining) => new(handle, _recipe, RecipeProcess.Forge, CraftingJobState.Active, 3, remaining, 1, 0, 1);
    private CraftingJobEvent Fact(long sequence, CraftingJobEventKind kind, CraftingJobHandle handle, int remaining, CraftingDeliveryStatus delivery = CraftingDeliveryStatus.NotApplicable) =>
        new(sequence, kind, Job(handle, remaining), Array.Empty<CraftingDeliverySnapshot>(), delivery, "test");
    [Fact]
    public void EnqueueDoesNotCompleteTargetAndPartialCancellationReleasesOnlyPendingAllocation()
    {
        var pin = new BlueprintPin(); pin.Set(_recipe, _station, "Test", 5); var handle = new CraftingJobHandle(_station, Guid.NewGuid());
        pin.Observe(Fact(1, CraftingJobEventKind.Queued, handle, 3)); Assert.Equal(5, pin.Remaining); Assert.Equal(3, pin.Queued);
        pin.Observe(Fact(2, CraftingJobEventKind.BatchObserved, handle, 2, CraftingDeliveryStatus.Verified)); Assert.Equal(4, pin.Remaining); Assert.Equal(2, pin.Queued);
        pin.Observe(Fact(3, CraftingJobEventKind.Cancelled, handle, 2)); Assert.Equal(4, pin.Remaining); Assert.Equal(0, pin.Queued);
    }
    [Fact]
    public void UnresolvedAndMissingBatchObservationsNeverInventDelivery()
    {
        var pin = new BlueprintPin(); pin.Set(_recipe, _station, "Test", 3); var handle = new CraftingJobHandle(_station, Guid.NewGuid()); pin.Include(Job(handle, 3));
        pin.Observe(Fact(1, CraftingJobEventKind.BatchObserved, handle, 2, CraftingDeliveryStatus.Unresolved));
        pin.Observe(Fact(2, CraftingJobEventKind.Finished, handle, 0));
        Assert.Equal(3, pin.Remaining); Assert.True(pin.Uncertain); Assert.Equal(0, pin.Queued);
    }
    [Fact]
    public void RepeatedOrOlderFactsCannotCompleteSameBatchTwice()
    {
        var pin = new BlueprintPin(); pin.Set(_recipe, _station, "Test", 3); var handle = new CraftingJobHandle(_station, Guid.NewGuid()); pin.Include(Job(handle, 3));
        pin.Observe(Fact(2, CraftingJobEventKind.BatchObserved, handle, 2, CraftingDeliveryStatus.Verified));
        pin.Observe(Fact(1, CraftingJobEventKind.BatchObserved, handle, 3, CraftingDeliveryStatus.Verified));
        pin.Observe(Fact(2, CraftingJobEventKind.BatchObserved, handle, 2, CraftingDeliveryStatus.Verified));
        Assert.Equal(2, pin.Remaining);
    }
    [Fact]
    public void ExistingJobsAllocateOnceAndForeignStationIsIgnored()
    {
        var pin = new BlueprintPin(); pin.Set(_recipe, _station, "Test", 2); var handle = new CraftingJobHandle(_station, Guid.NewGuid());
        pin.Include(Job(handle, 3)); pin.Include(Job(handle, 3)); Assert.Equal(2, pin.Queued);
        pin.Include(Job(new(new(_station.SessionId, Guid.NewGuid(), "Other"), Guid.NewGuid()), 3)); Assert.Equal(2, pin.Queued);
        pin.Clear(); Assert.Null(pin.Recipe); Assert.Equal(0, pin.Queued);
    }
    [Fact]
    public void FaultDoesNotAssumeStillRunningJobWasRemoved()
    {
        var pin = new BlueprintPin(); pin.Set(_recipe, _station, "Test", 3); var handle = new CraftingJobHandle(_station, Guid.NewGuid()); pin.Include(Job(handle, 3));
        pin.Observe(Fact(1, CraftingJobEventKind.OperationFaulted, handle, 3)); Assert.Equal(3, pin.Queued); Assert.True(pin.Uncertain);
        pin.Observe(Fact(2, CraftingJobEventKind.BatchObserved, handle, 2, CraftingDeliveryStatus.Verified)); Assert.Equal(2, pin.Remaining);
    }
}
