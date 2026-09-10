using System;
using BepInEx;
using VGModAPI;
using VGBlueprintPin.State;

namespace VGBlueprintPin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
[BepInDependency(ModApi.PluginId, "0.2.4")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vgblueprintpin", PluginName = "Blueprint Pin", PluginVersion = "0.4.2";
    private PinController? _controller;
    private bool _warned;
    private void Update()
    {
        try
        {
            if (_controller == null)
            {
                var services = ModApi.Services;
                if (!services.Lifecycle.SessionTracking.Availability.IsAvailable || !services.Recipes.Availability.IsAvailable ||
                    !services.RecipeQuotes.Availability.IsAvailable || !services.CraftingJobs.Availability.IsAvailable ||
                    !services.ForgeUi.Availability.IsAvailable || !services.Hud.Availability.IsAvailable)
                {
                    if (!_warned) Logger.LogWarning("Blueprint Pin requires available Mod API session, recipe and HUD services. Check API configuration and compatibility; unavailable bindings remain disabled.");
                    _warned = true; return;
                }
                _controller = new(PluginGuid, services.Lifecycle, services.Recipes, services.RecipeQuotes, services.CraftingJobs, services.ForgeUi, services.Hud);
            }
            // Event-driven: Drain is a cheap no-op unless an observation/action handler set the dirty flag.
            // No per-frame polling of reads, quotes or selection.
            _controller.Drain();
        }
        catch (Exception error) { Logger.LogError(error); _controller?.Dispose(); _controller = null; }
    }
    private void OnDestroy() { _controller?.Dispose(); _controller = null; }
}
