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
    }
    [Fact]
    public void StableRenderingKeepsRevisionAndUnavailableDataReplacesRequirements()
    {
        var api = new Fake(); using var controller = api.Create(); api.Pin!(api.Current!);
        Assert.Contains(api.Panel!.Rows, row => row.Detail.Contains("inaccessible cargo excluded"));
        Assert.Contains(api.Panel.Rows, row => row.Detail.Contains("0.004"));
        var updates = api.Updates; controller.Tick(); Assert.Equal(updates, api.Updates);
        api.QuoteStatus = RecipeQuoteStatus.RecipeUnavailable; controller.Tick();
        Assert.DoesNotContain(api.Panel.Rows, row => row.Id.StartsWith("ingredient"));
        Assert.Contains(api.Panel.Rows, row => row.Label == "Requirements unavailable");
    }
    [Fact]
    public void DisposeReleasesAllSubscriptionsAndRegistrations()
    {
        var api = new Fake(); var controller = api.Create(); controller.Dispose();
        Assert.Equal(5, api.Tokens.Count); Assert.All(api.Tokens, token => Assert.True(token.Disposed));
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
    private sealed class Token : IDisposable, IHudRegistration, IForgeActionRegistration
    {
        private readonly Fake _api;
        internal bool Disposed;
        internal Token(Fake api) { _api = api; }
        public void Dispose() => Disposed = true;
        public void Update(ForgeActionPresentation presentation) { }
        public void Update(HudButton? button, HudPanel? panel) { _api.Panel = panel; _api.Updates++; }
    }
    private sealed class Fake : ILifecycleApi, IRecipeCatalog, IRecipeQuotes, ICraftingJobs, IForgeUi, IModHud
    {
        internal int FailAt, Updates, ProducerCount;
        internal Action<HudInteraction>? Click;
        internal Action<LifecycleEvent>? Lifecycle;
        internal RecipeId? Opened;
        private int _registrations;
        internal List<Token> Tokens = new();
        internal Action<ForgeSelectionSnapshot>? Pin;
        internal HudPanel? Panel;
        internal RecipeQuoteStatus QuoteStatus = RecipeQuoteStatus.Available;
        private readonly RecipeId _recipe = new("vanilla", "variant");
        public RecipeStationHandle? CurrentStation { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Station");
        public ForgeSelectionSnapshot? Current => new(new(CurrentStation!.SessionId, Guid.NewGuid()), CurrentStation, _recipe, _recipe, new[] { _recipe }, 2, 1, new("Recipe", true));
        public SessionSnapshot? CurrentSession => null;
        public IReadOnlyList<CapabilityStatus> Capabilities => Array.Empty<CapabilityStatus>();
        public bool IsDispatchingCallbacks => false;
        public bool Visible => true;
        internal PinController Create() => new("test", this, this, this, this, this, this);
        private Token Acquire()
        {
            if (++_registrations == FailAt) throw new InvalidOperationException("Registration unavailable");
            var token = new Token(this); Tokens.Add(token); return token;
        }
        public IDisposable Subscribe(string owner, Action<LifecycleEvent> callback) { Lifecycle = callback; return Acquire(); }
        public IDisposable Subscribe(string owner, Action<CraftingJobEvent> callback) => Acquire();
        public IDisposable Subscribe(string owner, Action<ForgeSelectionChange> callback) => Acquire();
        public IHudRegistration Register(string pluginId, string localId, Action<HudInteraction> callback, int order = 0) { Click = callback; return Acquire(); }
        public IForgeActionRegistration RegisterAction(string pluginId, string localId, ForgeActionPresentation presentation, Action<ForgeSelectionSnapshot> callback, int order = 0) { Pin = callback; return Acquire(); }
        public ForgeNavigationStatus Open(RecipeId recipe) { Opened = recipe; return ForgeNavigationStatus.Selected; }
        public RecipeCatalogSnapshot Read(bool includeUnavailable = false) => new(RecipeCatalogStatus.Available, CurrentStation!.SessionId, "", Enumerable.Range(0, ProducerCount).Select(index =>
            new RecipeSnapshot(new("vanilla", "producer" + index), null, "Same display name", RecipeProcess.Forge, RecipeAvailability.Available, "",
                Array.Empty<RecipeResourceAmount>(), new[] { new RecipeResourceAmount(new("vanilla", "item", RecipeResourceKind.Item), 2) })));
        public CraftingJobListSnapshot Read(RecipeStationHandle station) => new(CraftingJobQueryStatus.Available, "", Array.Empty<CraftingJobSnapshot>());
        public RecipeQuote Quote(RecipeStationHandle station, RecipeId recipe, int batches = 1, RefineryInputPolicy refineryPolicy = RefineryInputPolicy.Manual) => new(QuoteStatus, "", station, recipe, batches, 1,
            QuoteStatus == RecipeQuoteStatus.Available ? new[] { new RecipeIngredientRequirement(new("vanilla", "item", RecipeResourceKind.Item), .004,
                new[] { new RecipeInventoryBalance(RecipeInventoryKind.PlayerArmory, null, true, 1), new RecipeInventoryBalance(RecipeInventoryKind.ShipCargo, null, false, null) }) } : Array.Empty<RecipeIngredientRequirement>(),
            Array.Empty<RecipeOutputPreview>(), Array.Empty<RecipeBlocker>(), creditsAvailable: 0, creditsRequired: 0);
        public RecipeQuote QuoteMaterialExtraction(RecipeStationHandle station, RecipeResourceId material, int count = 1) => throw new NotSupportedException();
    }
}
