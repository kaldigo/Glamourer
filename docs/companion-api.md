# Updating a saved design

Call `Aether.Glamourer.UpdateDesign` on the Dalamud framework thread. Its arguments are the target design GUID and a JSON string containing the complete appearance. The return value is an integer from `GlamourerApiEc`.

To refresh an outfit, use the complete design JSON with the desired equipment, dyes and application flags. A missing slot should have both `Apply` and `ApplyStain` set to false. The following call accepts the serialized result of editing a `Glamourer.GetDesignJObject` response.

```csharp
var update = pluginInterface.GetIpcSubscriber<Guid, string, int>("Aether.Glamourer.UpdateDesign");
var result = (GlamourerApiEc)update.InvokeFunc(targetId, appearanceJson);
```

A successful call updates the existing object's appearance, application flags and materials and queues it for saving. Its GUID, name, tags, links, mods and automation assignments survive the update. The outfit takes effect on the next application. A missing design returns `DesignNotFound`, a write-protected design returns `NothingDone`, a call off the framework thread returns `InvalidState`, and an unreadable appearance returns `CouldNotParse`.

To verify in game, assign a design directly to a job with its boots and boot dyes disabled. Update the same ID with those flags enabled while on that job. Check that the actor stays as dressed, switch away and back, and verify the new boots and dyes apply. Restart the plugin and check the design ID, flags and job assignment again.
