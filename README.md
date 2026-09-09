# Blueprint Pin

Track a Forge recipe's remaining crafting batches while gathering ingredients. Requires BepInEx 5 and **VGModAPI 0.2.4 or newer**. Blueprint Pin 0.4 uses the typed `ModApi.Services` facade and events; it is not compatible with API 0.1.x. Install Mod API separately; this plugin does not bundle it or any game/Unity references.

Recipe and HUD services initialize automatically in the required API version. Unsupported game hashes/bindings remain unavailable. Crafting command integration is not required: Blueprint Pin never queues or cancels work itself.

## Using the pin

- In the Forge, choose an exact variant and 1–10,000 batches, then select **Pin blueprint** beside the result.
- A compact Forge-style widget shows the recipe and ingredients for batches **not already allocated to the queue**. Required quantities and available stock have separate columns; shortage counts are red, sufficient counts green, and unknown stock displays `?`. Allocated work and completion appear as plain progress messages, not technical queue counters.
- Ingredient icons and item tooltips use Mod API presentation. Select an ingredient to open its sole available Forge producer, or choose among alternatives. Refining alternatives are identified but require the native Refinery; Forge navigation cannot open them. No producer is not an error or an invented recipe.
- The integrated **Show in Forge** action opens to the exact variant at the current station. The target remains scoped to its original station; pin again to change the target station.
- Close the panel to clear the pin; closing the producer chooser returns to the pin. Selecting **Pinned** with the same variant/station and remaining quantity unpins it.

## Target policy

A target counts **future verified batches**, not output units. A batch can produce multiple items, materials, fractional quantities or generated outputs. Queue admission does not complete a batch.

Existing matching jobs at the pinned station allocate their remaining batches once. Newly queued jobs can allocate unmet work. Verified batch delivery reduces the target and its allocation by one. Cancelling releases unfinished allocation without restoring already completed batches. Unresolved delivery, missing observations or faults produce a reconciliation warning rather than guessed success. Faults do not assume a still-running job was removed. Repeated/older facts cannot complete the same batch twice.

Availability is advisory and not a reservation. Inaccessible cargo is excluded explicitly; unknown data is never presented as known stock. A missing recipe or unavailable service shows an unavailable state rather than stale requirements. Pins are session-only and clear on session replacement/failure; they do not serialize native handles or add save callbacks. Native job persistence belongs to the game/API, not the pin.

## Build and checks

.NET SDK 10 is required for tests. Build the Mod API abstractions at the required version, then:

```sh
make build test CONFIGURATION=Release API_ABSTRACTIONS=/path/to/VGModAPI.Abstractions.dll
```

Install the release archive's `VGBlueprintPin` folder into `BepInEx/plugins/`. Keep `vgblueprintpin.vgmod.json` beside `VGBlueprintPin.dll`: it provides the Mods-menu description, author, project link and stable update feed. Remove an older standalone copy of the DLL when switching to the folder layout; do not load both. `make package CONFIGURATION=Release` creates the ZIP and matching `update.json` from the project version. Publish both as release assets; the update feed must not advertise a release without its archive. Published assets are not overwritten. `make deploy` installs the DLL and metadata into the local game installation; use it only for an authorized deployment. CI builds public contracts from a pinned API source revision; no game, UI or TextMeshPro assembly is used.

The consumer uses public recipes, quotes, Forge actions/navigation, job events and shared HUD contracts. Unity is used only for the BepInEx host/tick clock, not game access or presentation. No Harmony patch, private reflection, retained game recipe or cloned ingredient widget is used.

Correctness tests cover pin accounting, unavailable data, producer choices and presentation models. Build/test results are not a claim that every game resolution or font combination has been exercised.
