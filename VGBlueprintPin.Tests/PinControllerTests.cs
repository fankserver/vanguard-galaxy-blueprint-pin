using System;
using System.Collections.Generic;
using System.Linq;
using VGModAPI;
using VGBlueprintPin.State;
using Xunit;

namespace VGBlueprintPin.Tests;

public sealed class PinControllerTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void FailedRegistrationRollsBackEveryAcquiredToken(int failure)
    {
        var api = new Fake { FailAt = failure };
        Assert.Throws<InvalidOperationException>(() => api.Create());
        Assert.All(api.Tokens, token => Assert.True(token.Disposed));
        Assert.Equal(0, api.ActiveSubscriptions); Assert.Null(api.Lifecycle);
    }
    [Fact]
    public void StableRenderingKeepsRevisionAndUnavailableDataReplacesRequirements()
    {
        var api = new Fake(); using var controller = api.Create(); api.Pin!(api.Current!);
        Assert.Contains(api.Panel!.Rows, row => row.Tooltip.Contains("inaccessible cargo excluded"));
        Assert.Contains(api.Panel.Rows, row => row.IngredientAmounts?.RequiredText == "0.004");
        var updates = api.Updates; controller.Tick(); Assert.Equal(updates, api.Updates);
        api.QuoteStatus = RecipeQuoteStatus.RecipeUnavailable; controller.Tick();
        Assert.DoesNotContain(api.Panel.Rows, row => row.Id.StartsWith("ingredient"));
        Assert.Contains(api.Panel.Rows, row => row.Label == "Requirements unavailable");
    }
    [Fact]
    public void ThresholdCrossingRefreshesIngredientSufficiency()
    {
        var api = new Fake { Available = .003999999999 }; using var controller = api.Create(); api.Pin!(api.Current!);
        Assert.False(api.Panel!.Rows.Single().IngredientAmounts!.Sufficient);
        var updates = api.Updates;
        api.Available = .004000000001; controller.Tick();
        Assert.True(api.Updates > updates);
        Assert.True(api.Panel!.Rows.Single().IngredientAmounts!.Sufficient);
    }

    [Fact]
    public void SuccessfulOpenDoesNotExposeNavigationStatus()
    {
        var api = new Fake(); using var controller = api.Create(); api.Pin!(api.Current!);
        api.Click!(new(api.CurrentStation!.SessionId, HudInteractionKind.Button, null, 1));
        Assert.NotNull(api.Opened);
        Assert.DoesNotContain(api.Panel!.Rows, row => row.Id == "status");
        Assert.Equal("Recipe", api.Panel.Title);
        Assert.Equal("Show in Forge", api.Button!.Label);
        Assert.Equal("Pinned", api.Action!.Label);
    }

    [Fact]
    public void DisposeReleasesAllSubscriptionsAndRegistrations()
    {
        var api = new Fake(); var controller = api.Create(); controller.Dispose();
        Assert.Equal(5, api.Tokens.Count); Assert.All(api.Tokens, token => Assert.True(token.Disposed));
        Assert.Equal(0, api.ActiveSubscriptions); Assert.Null(api.Lifecycle);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ProducerSelectionUsesIdentityAndHandlesAlternatives(int count)
    {
        var api = new Fake { ProducerCount = count }; using var controller = api.Create(); api.Pin!(api.Current!);
        api.Click!(new(api.CurrentStation!.SessionId, HudInteractionKind.Row, "ingredient0", 1));
        if (count == 0) Assert.Contains(api.Panel!.Rows, row => row.Id == "none");
        else if (count == 1) Assert.Equal("producer0", api.Opened!.LocalId);
        else
        {
            Assert.Null(api.Opened); Assert.Equal(2, api.Panel!.Rows.Count);
            Assert.NotEqual(api.Panel.Rows[0].Detail, api.Panel.Rows[1].Detail);
            api.Click!(new(api.CurrentStation.SessionId, HudInteractionKind.Row, "producer1", 2));
            Assert.Equal("producer1", api.Opened!.LocalId);
        }
    }
    [Fact]
    public void SessionReplacementClearsPinAndOwnedModels()
    {
        var api = new Fake(); using var controller = api.Create(); api.Pin!(api.Current!);
        api.Lifecycle!(new(LifecycleEventKind.SessionStarting, null)); controller.Tick(); Assert.Null(api.Panel);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedQueueObservationNeverClaimsAllocationDerivedRequirements(bool initiallyAllocated)
    {
        var api = new Fake { Allocate = initiallyAllocated };
        if (!initiallyAllocated) api.JobStatus = CraftingJobQueryStatus.NativeFailure;
        using var controller = api.Create(); api.Pin!(api.Current!);
        api.JobStatus = CraftingJobQueryStatus.NativeFailure; controller.Tick();
        Assert.Contains(api.Panel!.Rows, row => row.Id == "jobs-unavailable");
        Assert.DoesNotContain(api.Panel.Rows, row => row.Id.StartsWith("ingredient"));
        Assert.DoesNotContain(api.Panel.Rows, row => row.Id == "progress" || row.Id == "crafting");
        api.JobStatus = CraftingJobQueryStatus.Available; controller.Tick();
        Assert.DoesNotContain(api.Panel.Rows, row => row.Id == "jobs-unavailable");
    }
    private sealed class Token : IDisposable, IHudRegistration, IForgeActionRegistration
    {
        private readonly Fake _api;
        internal bool Disposed;
        internal Token(Fake api) { _api = api; }
        public void Dispose() => Disposed = true;
        public void Update(ForgeActionPresentation presentation) { _api.Action = presentation; }
        public void Update(HudButton? button, HudPanel? panel) { _api.Panel = panel; _api.Button = button; _api.Updates++; }
    }
    private sealed class Fake : ILifecycleService, IRecipeService, IRecipeQuoteService, ICraftingJobService, IForgeUiService, IHudService
    {
        internal int FailAt, Updates, ProducerCount;
        internal bool Allocate;
        internal double Available = 1;
        internal CraftingJobQueryStatus JobStatus = CraftingJobQueryStatus.Available;
        private readonly Guid _jobId = Guid.NewGuid();
        internal Action<HudInteraction>? Click;
        internal Action<LifecycleEvent>? Lifecycle;
        internal RecipeId? Opened;
        private int _registrations;
        internal List<Token> Tokens = new();
        internal Action<ForgeSelectionSnapshot>? Pin;
        internal HudPanel? Panel;
        internal HudButton? Button;
        internal ForgeActionPresentation? Action;
        internal RecipeQuoteStatus QuoteStatus = RecipeQuoteStatus.Available;
        private readonly RecipeId _recipe = new("vanilla", "variant");
        public RecipeStationHandle? CurrentStation { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Station");
        public ForgeSelectionSnapshot? Current => new(new(CurrentStation!.SessionId, Guid.NewGuid()), CurrentStation, _recipe, _recipe, new[] { _recipe }, 2, 1, new("Recipe", true));
        public SessionSnapshot? CurrentSession => null;
        public ServiceAvailability Availability => ServiceAvailability.Available;
        public event Action<ServiceAvailability>? AvailabilityChanged { add { } remove { } }
        public IServiceStatus SessionTracking => this;
        public IServiceStatus SaveOutcomes => this;
        private readonly Dictionary<Delegate, Token> _subscriptions = new();
        internal int ActiveSubscriptions => _subscriptions.Count;
        public bool IsDispatchingCallbacks => false;
        public bool Visible => true;
        internal PinController Create() => new("test", this, this, this, this, this, this);
        private Token Acquire()
        {
            if (++_registrations == FailAt) throw new InvalidOperationException("Registration unavailable");
            var token = new Token(this); Tokens.Add(token); return token;
        }
        event Action<LifecycleEvent>? ILifecycleService.Changed
        {
            add { _subscriptions.Add(value!, Acquire()); Lifecycle += value; }
            remove { Lifecycle -= value; Release(value); }
        }
        event Action<CraftingJobEvent>? ICraftingJobService.Changed
        {
            add { _subscriptions.Add(value!, Acquire()); }
            remove { Release(value); }
        }
        event Action<ForgeSelectionChange>? IForgeUiService.Changed
        {
            add { _subscriptions.Add(value!, Acquire()); }
            remove { Release(value); }
        }
        private void Release(Delegate? handler)
        {
            if (handler != null && _subscriptions.Remove(handler, out var token)) token.Dispose();
        }
        public IHudRegistration Register(string pluginId, string localId, Action<HudInteraction> callback, int order = 0) { Click = callback; return Acquire(); }
        public IForgeActionRegistration RegisterAction(string pluginId, string localId, ForgeActionPresentation presentation, Action<ForgeSelectionSnapshot> callback, int order = 0) { Pin = callback; return Acquire(); }
        public ForgeNavigationStatus Open(RecipeId recipe) { Opened = recipe; return ForgeNavigationStatus.Selected; }
        public RecipeCatalogSnapshot Read(bool includeUnavailable = false) => new(RecipeCatalogStatus.Available, CurrentStation!.SessionId, "", Enumerable.Range(0, ProducerCount).Select(index =>
            new RecipeSnapshot(new("vanilla", "producer" + index), null, "Same display name", RecipeProcess.Forge, RecipeAvailability.Available, "",
                Array.Empty<RecipeResourceAmount>(), new[] { new RecipeResourceAmount(new("vanilla", "item", RecipeResourceKind.Item), 2) })));
        public CraftingJobListSnapshot Read(RecipeStationHandle station) => new(JobStatus, "", Allocate && JobStatus == CraftingJobQueryStatus.Available
            ? new[] { new CraftingJobSnapshot(new(station, _jobId), _recipe, RecipeProcess.Forge, CraftingJobState.Active, 2, 2, 1, 0, 1) }
            : Array.Empty<CraftingJobSnapshot>());
        public RecipeQuote Quote(RecipeStationHandle station, RecipeId recipe, int batches = 1, RefineryInputPolicy refineryPolicy = RefineryInputPolicy.Manual) => new(QuoteStatus, "", station, recipe, batches, 1,
            QuoteStatus == RecipeQuoteStatus.Available ? new[] { new RecipeIngredientRequirement(new("vanilla", "item", RecipeResourceKind.Item), .004,
                new[] { new RecipeInventoryBalance(RecipeInventoryKind.PlayerArmory, null, true, Available), new RecipeInventoryBalance(RecipeInventoryKind.ShipCargo, null, false, null) }) } : Array.Empty<RecipeIngredientRequirement>(),
            Array.Empty<RecipeOutputPreview>(), Array.Empty<RecipeBlocker>(), creditsAvailable: 0, creditsRequired: 0);
        public RecipeQuote QuoteMaterialExtraction(RecipeStationHandle station, RecipeResourceId material, int count = 1) => throw new NotSupportedException();
    }
}
