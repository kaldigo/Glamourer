using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Designs;
using Glamourer.Events;
using Glamourer.Services;
using Luna;

namespace Glamourer.Api;

public sealed class SavedDesignApi(DesignManager designs, DesignConverter converter, CustomizeService customizations,
    SaveService saves, DesignChanged changed, IFramework framework) : IApiService
{
    // Replace appearance and application flags on the same object referenced by automation.
    public int Update(Guid id, string json)
    {
        if (!framework.IsInFrameworkUpdateThread)
            return (int)GlamourerApiEc.InvalidState;
        if (designs.Designs.ByIdentifier(id) is not { } design)
            return (int)GlamourerApiEc.DesignNotFound;
        if (design.WriteProtected())
            return (int)GlamourerApiEc.NothingDone;

        DesignBase? source;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject input || input["Equipment"] is not JsonObject
             || input["Customize"] is not JsonObject)
                return (int)GlamourerApiEc.CouldNotParse;

            // Loading links registers deferred references. The target retains its own links.
            input.Remove("Links");
            source = converter.FromJsonElement(JsonSerializer.SerializeToElement(input), true, true);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return (int)GlamourerApiEc.CouldNotParse;
        }

        if (source is null)
            return (int)GlamourerApiEc.CouldNotParse;

        design.SetDesignData(customizations, source.DesignData);
        design.Application = source.Application;
        var materials = design.GetMaterialDataRef();
        materials.Clear();
        foreach (var (key, value) in source.Materials)
            materials.AddOrUpdateValue(key, value);

        design.LastEdit = DateTimeOffset.UtcNow;
        saves.QueueSave(design);
        changed.Invoke(new DesignChanged.Arguments(DesignChanged.Type.Other, design));
        return (int)GlamourerApiEc.Success;
    }
}
