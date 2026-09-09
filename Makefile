CONFIG ?= Debug
CONFIGURATION ?= $(CONFIG)
DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)
API_ABSTRACTIONS ?= ../vanguard-galaxy-api/VGModAPI.Abstractions/bin/Release/netstandard2.1/VGModAPI.Abstractions.dll
GAME_DIR ?= /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
BUILDDIR := VGBlueprintPin/bin/$(CONFIGURATION)/netstandard2.1

.PHONY: all link-api build test package deploy clean
all: build
link-api:
	@test -f "$(API_ABSTRACTIONS)" || { echo 'Build Mod API 0.2.0+ abstractions or set API_ABSTRACTIONS to its DLL.'; exit 1; }
	@mkdir -p VGBlueprintPin/lib
	@ln -sf "$(abspath $(API_ABSTRACTIONS))" VGBlueprintPin/lib/VGModAPI.Abstractions.dll
build: link-api
	$(DOTNET) build VGBlueprintPin/VGBlueprintPin.csproj -c $(CONFIGURATION)
test: link-api
	$(DOTNET) test VGBlueprintPin.Tests/VGBlueprintPin.Tests.csproj -c $(CONFIGURATION)
package: build
	python3 tools/package.py --configuration $(CONFIGURATION)
deploy: build
	@test -d "$(GAME_DIR)/BepInEx/plugins"
	@test ! -f "$(GAME_DIR)/BepInEx/plugins/VGBlueprintPin.dll" || { echo 'Back up and remove the old standalone DLL before folder deployment.'; exit 1; }
	mkdir -p "$(GAME_DIR)/BepInEx/plugins/VGBlueprintPin"
	cp "$(BUILDDIR)/VGBlueprintPin.dll" vgblueprintpin.vgmod.json "$(GAME_DIR)/BepInEx/plugins/VGBlueprintPin/"
clean:
	$(DOTNET) clean VGBlueprintPin/VGBlueprintPin.csproj
	rm -rf VGBlueprintPin/bin VGBlueprintPin/obj VGBlueprintPin.Tests/bin VGBlueprintPin.Tests/obj
