using System;
using BepInEx;
using VGModAPI;
using VGBlueprintPin.State;

namespace VGBlueprintPin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
[BepInDependency(ModApi.PluginId, "0.1.38")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vgblueprintpin", PluginName = "Blueprint Pin", PluginVersion = "0.2.0";
    private PinController? _controller;
    private float _next;
    private bool _warned;
    private void Update()
    {
        if (UnityEngine.Time.unscaledTime < _next) return;
        _next = UnityEngine.Time.unscaledTime + .5f;
        try
        {
            if (_controller == null)
            {
                if (ModApi.Current == null || ModApi.Recipes == null || ModApi.RecipeQuotes == null || ModApi.CraftingJobs == null || ModApi.ForgeUi == null || ModApi.Hud == null)
                {
                    if (!_warned) Logger.LogWarning("Blueprint Pin requires Mod API recipe and HUD integration. Enable [Recipes] Enabled and [Hud] Enabled, then restart. Unavailable bindings remain disabled.");
                    _warned = true; return;
                }
                _controller = new(PluginGuid, ModApi.Current, ModApi.Recipes, ModApi.RecipeQuotes, ModApi.CraftingJobs, ModApi.ForgeUi, ModApi.Hud);
            }
            _controller.Tick();
        }
        catch (Exception error) { Logger.LogError(error); _controller?.Dispose(); _controller = null; }
    }
    private void OnDestroy() { _controller?.Dispose(); _controller = null; }
}
