using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VGModAPI;

namespace VGBlueprintPin.State;

internal sealed class PinController : IDisposable
{
    private readonly BlueprintPin _pin = new();
    private readonly IRecipeCatalog _catalog;
    private readonly IRecipeQuotes _quotes;
    private readonly ICraftingJobs _jobs;
    private readonly IForgeUi _ui;
    private readonly IHudRegistration _hud;
    private readonly IForgeActionRegistration _action;
    private readonly IDisposable _lifetime, _jobEvents, _selection;
    private readonly Dictionary<string, RecipeResourceId> _resources = new();
    private readonly Dictionary<string, RecipeSnapshot> _choices = new();
    private string _status = "", _fingerprint = "";
    private bool _choosing;
    private CraftingJobQueryStatus _jobStatus = CraftingJobQueryStatus.SessionUnavailable;
    internal PinController(string provider, ILifecycleApi lifecycle, IRecipeCatalog catalog, IRecipeQuotes quotes, ICraftingJobs jobs, IForgeUi ui, IModHud hud)
    {
        _catalog = catalog; _quotes = quotes; _jobs = jobs; _ui = ui;
        var owned = new List<IDisposable>();
        try
        {
        _hud = hud.Register(provider, "blueprint", OnHud); owned.Add(_hud);
        _action = ui.RegisterAction(provider, "pin", new("Pin", "Track verified future batches", enabled: false), PinSelection, 0);
        owned.Add(_action);
        _selection = ui.Subscribe(provider, _ => UpdateAction()); owned.Add(_selection);
        _jobEvents = jobs.Subscribe(provider, fact => _pin.Observe(fact)); owned.Add(_jobEvents);
        _lifetime = lifecycle.Subscribe(provider, fact =>
        {
            if (fact.Kind is LifecycleEventKind.SessionStarting or LifecycleEventKind.SessionInvalidated or LifecycleEventKind.SessionStartFailed)
            { _pin.Clear(); _choosing = false; _status = ""; _hud.Update(null, null); _fingerprint = ""; }
        });
        owned.Add(_lifetime);
        UpdateAction();
        }
        catch
        {
            for (var index = owned.Count - 1; index >= 0; index--)
                try { owned[index].Dispose(); } catch { /* Preserve the initialization failure after attempting every cleanup. */ }
            throw;
        }
    }
    private void PinSelection(ForgeSelectionSnapshot selection)
    {
        if (selection.Batches < 1 || selection.Batches > 10000) return;
        if (_pin.Recipe?.Equals(selection.SelectedRecipe) == true && _pin.Station?.Equals(selection.Station) == true && _pin.Remaining == selection.Batches) _pin.Clear();
        else
        {
            _pin.Set(selection.SelectedRecipe, selection.Station, selection.Presentation.DisplayName, selection.Batches);
            IncludeExisting();
        }
        _status = ""; _choosing = false; _fingerprint = ""; Tick();
    }
    private void IncludeExisting()
    {
        if (_pin.Station == null) return;
        var jobs = _jobs.Read(_pin.Station);
        _jobStatus = jobs.Status;
        if (jobs.Status == CraftingJobQueryStatus.Available) foreach (var job in jobs.Jobs) _pin.Include(job);
    }
    private void UpdateAction()
    {
        var selection = _ui.Current;
        var valid = selection != null && selection.Batches is >= 1 and <= 10000;
        var same = selection != null && _pin.Recipe?.Equals(selection.SelectedRecipe) == true && _pin.Remaining == selection.Batches && _pin.Station?.Equals(selection.Station) == true;
        _action.Update(new(same ? "Unpin" : "Pin", valid ? "Target future verified batches, not output units" : "Choose 1–10,000 batches", valid, true));
    }
    internal void Tick()
    {
        UpdateAction();
        if (_pin.Recipe == null) { _hud.Update(null, null); _fingerprint = ""; return; }
        IncludeExisting();
        var rows = new List<HudRow>(); _resources.Clear();
        if (_choosing)
        {
            foreach (var choice in _choices) rows.Add(new(choice.Key, Short(choice.Value.DisplayName),
                Short(choice.Key + " · " + choice.Value.Process + " · " + (choice.Value.Rarity ?? "rarity unspecified") + " · " + choice.Value.Id.LocalId),
                Short(choice.Value.Id.ProviderId + ":" + choice.Value.Id.LocalId) + (choice.Value.Process == RecipeProcess.Forge ? " — Open this exact producer/variant" : " — Refining route: use the Refinery"), clickable: choice.Value.Process == RecipeProcess.Forge));
            if (rows.Count == 0) rows.Add(new("none", "No available crafting producer", "Gather, loot or buy this ingredient"));
        }
        else
        {
            rows.Add(new("progress", $"{_pin.Remaining} batches remaining", _jobStatus == CraftingJobQueryStatus.Available ? $"{_pin.Queued} allocated in queue" : "Queue allocation unavailable"));
            if (_jobStatus != CraftingJobQueryStatus.Available) rows.Add(new("jobs-unavailable", "Allocation-derived requirements unavailable", _jobStatus.ToString()));
            if (_pin.Uncertain) rows.Add(new("uncertain", "Unverified work observed", "Reconcile inventory before pinning a new target"));
            if (_status.Length != 0) rows.Add(new("status", Short(_status)));
            var unallocated = Math.Max(0, _pin.Remaining - _pin.Queued);
            if (unallocated > 0 && _jobStatus == CraftingJobQueryStatus.Available)
            {
                var quote = _quotes.Quote(_pin.Station!, _pin.Recipe, unallocated);
                if (quote.Status != RecipeQuoteStatus.Available) rows.Add(new("unavailable", "Requirements unavailable", quote.Status.ToString()));
                else if (quote.Inputs.Count > 28) rows.Add(new("limit", "Too many ingredients to display safely"));
                else foreach (var input in quote.Inputs)
                {
                    var id = "ingredient" + _resources.Count; _resources.Add(id, input.Resource);
                    var unavailable = input.Inventories.Any(inventory => !inventory.Accessible) ? "; inaccessible cargo excluded" : "";
                    var detail = $"need {Number(input.Required)} / have {Number(input.Available)}{unavailable}";
                    if (input.Resource.Kind is not (RecipeResourceKind.Item or RecipeResourceKind.RefinedMaterial))
                    {
                        rows.Add(new(id, Short(input.Resource.LocalId), Short(detail), "No supported icon/tooltip presentation for this resource kind", clickable: true));
                        continue;
                    }
                    var kind = input.Resource.Kind == RecipeResourceKind.Item ? HudPresentationKind.Item : HudPresentationKind.RefinedMaterial;
                    rows.Add(new(id, "", Short(detail), "Choose an available producer", new(input.Resource.ProviderId, input.Resource.LocalId, kind,
                        (int)Math.Max(1, Math.Min(int.MaxValue, input.Required))), true));
                }
            }
        }
        var title = _choosing ? "Choose producer" : Short(_pin.Name + " · " + _pin.Station!.DisplayName);
        var fields = new[] { title, _pin.Recipe.ProviderId, _pin.Recipe.LocalId }.Concat(rows.SelectMany(row => new[] {
            row.Id, row.Label, row.Detail, row.Tooltip, row.Clickable.ToString(), row.Presentation?.ProviderId ?? "", row.Presentation?.LocalId ?? "",
            row.Presentation?.Kind.ToString() ?? "", row.Presentation?.TooltipCount.ToString(CultureInfo.InvariantCulture) ?? "" }));
        var fingerprint = string.Concat(fields.Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));
        if (fingerprint == _fingerprint) return;
        _hud.Update(new("Open pinned recipe", "Open the exact variant at the current station; the target remains scoped to its original station"), new(title, rows,
            new(_pin.Recipe.ProviderId, _pin.Recipe.LocalId, HudPresentationKind.ForgeRecipe)));
        _fingerprint = fingerprint;
    }
    private void OnHud(HudInteraction interaction)
    {
        if (_pin.Recipe == null) return;
        if (interaction.Kind == HudInteractionKind.ClosePanel)
        { if (_choosing) _choosing = false; else _pin.Clear(); }
        else if (interaction.Kind == HudInteractionKind.Button) Navigate(_pin.Recipe);
        else if (_choosing && interaction.RowId != null && _choices.TryGetValue(interaction.RowId, out var choice)) { Navigate(choice.Id); _choosing = false; }
        else if (interaction.RowId != null && _resources.TryGetValue(interaction.RowId, out var resource))
        {
            var catalog = _catalog.Read(); _choices.Clear();
            if (catalog.Status != RecipeCatalogStatus.Available) _status = "Producer lookup unavailable: " + catalog.Status;
            else
            {
                var candidates = catalog.FindProducers(resource);
                if (candidates.Count == 1 && candidates[0].Process == RecipeProcess.Forge) Navigate(candidates[0].Id);
                else if (candidates.Count > 32) _status = "Too many producers; choose in the native Forge";
                else { foreach (var candidate in candidates) _choices.Add("producer" + _choices.Count, candidate); _choosing = true; }
            }
        }
        _fingerprint = ""; Tick();
    }
    private void Navigate(RecipeId recipe) => _status = "Navigation: " + _ui.Open(recipe);
    private static string Number(double? value) => value?.ToString("G9", CultureInfo.InvariantCulture) ?? "unknown";
    private static string Short(string value) => value.Length <= 256 ? value : value.Substring(0, 253) + "...";
    public void Dispose() { _selection.Dispose(); _jobEvents.Dispose(); _lifetime.Dispose(); _action.Dispose(); _hud.Dispose(); _pin.Clear(); }
}
