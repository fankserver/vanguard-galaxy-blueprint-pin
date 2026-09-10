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
    public const string PluginGuid = "vgblueprintpin", PluginName = "Blueprint Pin", PluginVersion = "0.4.3";
    private PinController? _controller;
    private bool _warned;
    // Pure one-shot initialization gate, not a render loop.
    // Nothing is rendered, read or polled on the frame path: after the controller is
    // created this returns immediately and stays inert. The single retry is only to
    // wait for ModApi.Services to be published (the API exposes no "services ready"
    // event; every _controller.Drain-style deferred render was removed) — rendering
    // happens synchronously inside event handlers (see PinController).
    private void Update()
    {
        if (_controller != null) return;
        try
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
        catch (Exception error) { Logger.LogError(error); _controller?.Dispose(); _controller = null; }
    }
    private void OnDestroy() { _controller?.Dispose(); _controller = null; }
}
