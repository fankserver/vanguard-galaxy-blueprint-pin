using System;
using System.Collections.Generic;
using System.Linq;
using VGModAPI;

namespace VGBlueprintPin.State;

/// <summary>Consumer policy: a target counts future verified native batches, never output units or queue admission.</summary>
internal sealed class BlueprintPin
{
    private readonly Dictionary<CraftingJobHandle, Allocation> _jobs = new();
    private readonly HashSet<CraftingJobHandle> _seen = new();
    public RecipeId? Recipe { get; private set; }
    public RecipeStationHandle? Station { get; private set; }
    public string Name { get; private set; } = "";
    public int Remaining { get; private set; }
    public int Queued => _jobs.Values.Sum(value => value.Allocated);
    public bool Uncertain { get; private set; }
    private long _lastSequence;
    public void Set(RecipeId recipe, RecipeStationHandle station, string name, int batches)
    {
        if (batches < 1 || batches > 10000) throw new ArgumentOutOfRangeException(nameof(batches));
        Clear(); Recipe = recipe; Station = station; Name = name; Remaining = batches;
    }
    public void Clear()
    { Recipe = null; Station = null; Name = ""; Remaining = 0; Uncertain = false; _lastSequence = 0; _jobs.Clear(); _seen.Clear(); }
    public void Include(CraftingJobSnapshot job)
    {
        if (Recipe == null || !Recipe.Equals(job.Recipe) || Station == null || !Station.Equals(job.Handle.Station) || _seen.Contains(job.Handle)) return;
        if (_seen.Count >= 4096) { Uncertain = true; return; }
        _seen.Add(job.Handle);
        var available = Remaining - Queued;
        if (available <= 0 || job.RemainingBatches <= 0) return;
        _jobs.Add(job.Handle, new Allocation(job.RemainingBatches, Math.Min(available, job.RemainingBatches)));
    }
    public void Observe(CraftingJobEvent fact)
    {
        if (fact.Sequence <= _lastSequence) return;
        _lastSequence = fact.Sequence;
        if (fact.Kind == CraftingJobEventKind.Queued) { Include(fact.Job); return; }
        if (!_jobs.TryGetValue(fact.Job.Handle, out var allocation)) return;
        if (fact.Kind == CraftingJobEventKind.BatchObserved)
        {
            var delta = allocation.LastRemaining - fact.Job.RemainingBatches;
            if (delta == 0) return;
            if (delta < 0) { Uncertain = true; return; }
            allocation.LastRemaining = fact.Job.RemainingBatches;
            if (delta != 1) { Uncertain = true; allocation.Allocated = Math.Max(0, allocation.Allocated - Math.Max(0, delta)); return; }
            if (allocation.Allocated > 0)
            {
                allocation.Allocated--;
                if (fact.DeliveryStatus == CraftingDeliveryStatus.Verified) Remaining = Math.Max(0, Remaining - 1);
                else Uncertain = true;
            }
        }
        else if (fact.Kind == CraftingJobEventKind.OperationFaulted) Uncertain = true;
        else if (fact.Kind is CraftingJobEventKind.Cancelled or CraftingJobEventKind.Finished or CraftingJobEventKind.Invalidated)
        {
            if (fact.Kind is CraftingJobEventKind.Invalidated or CraftingJobEventKind.OperationFaulted || fact.Kind == CraftingJobEventKind.Finished && allocation.Allocated > 0) Uncertain = true;
            _jobs.Remove(fact.Job.Handle);
        }
    }
    private sealed class Allocation
    {
        internal int LastRemaining, Allocated;
        internal Allocation(int remaining, int allocated) { LastRemaining = remaining; Allocated = allocated; }
    }
}
