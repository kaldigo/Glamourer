using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Glamourer.Services;
using Lumina.Excel.Sheets;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Cabinet = Lumina.Excel.Sheets.Cabinet;

namespace Glamourer.Unlocks;

public sealed class WardrobeOwnership : IService, IDisposable
{
    private sealed record Snapshot(HashSet<uint>? Dresser = null, HashSet<uint>? Armoire = null);

    private readonly IFramework _framework;
    private readonly IPlayerState _player;
    private readonly IClientState _client;
    private readonly string _directory;
    private readonly (uint Row, uint Item)[] _cabinet;
    private readonly Dictionary<uint, uint[]> _outfits;
    private readonly HashSet<uint> _exceptions;
    private Snapshot _snapshot = new();
    private ulong _character;
    private long _nextScan;
    private bool _dirty;

    public WardrobeOwnership(IFramework framework, IPlayerState player, IClientState client, IDalamudPluginInterface pi, IDataManager data)
    {
        _player = player;
        _client = client;
        _framework = framework;
        _directory = pi.GetPluginConfigDirectory();
        _cabinet = data.GetExcelSheet<Cabinet>().Where(c => c.Item.RowId != 0).Select(c => (c.RowId, c.Item.RowId)).ToArray();
        _outfits = data.GetExcelSheet<MirageStoreSetItem>().ToDictionary(r => r.RowId, r => new[]
        {
            r.MainHand.RowId, r.OffHand.RowId, r.Head.RowId, r.Body.RowId, r.Hands.RowId, r.Legs.RowId,
            r.Feet.RowId, r.Earrings.RowId, r.Necklace.RowId, r.Bracelets.RowId, r.Ring.RowId,
        });
        using var catalog = typeof(WardrobeOwnership).Assembly.GetManifestResourceStream("Glamourer.WardrobeExceptions.json")!;
        _exceptions = JsonSerializer.Deserialize<HashSet<uint>>(catalog)!;
        Update(framework);
        framework.Update += Update;
    }

    public bool Confirmed => CurrentCharacter && _snapshot.Dresser != null && _snapshot.Armoire != null;
    private bool CurrentCharacter => _client.IsLoggedIn && _character != 0 && _character == _player.ContentId;

    public bool Allows(EquipSlot slot, EquipItem item)
    {
        if (item.Id == ItemManager.NothingId(slot) || item.Id == ItemManager.NothingId(item.Type)
         || item.Id == ItemManager.SmallclothesId(slot))
            return true;

        var id = item.ItemId.Id;
        return _exceptions.Contains(id) || CurrentCharacter && (_snapshot.Dresser?.Contains(id) == true || _snapshot.Armoire?.Contains(id) == true);
    }

    private string CacheFile => Path.Combine(_directory, $"wardrobe-{_character:X16}.json");

    private unsafe void Update(IFramework framework)
    {
        var character = _client.IsLoggedIn ? _player.ContentId : 0;
        if (character != _character)
        {
            Save();
            _character = character;
            _dirty = false;
            _snapshot = new();
            _nextScan = 0;
            try
            {
                if (character != 0 && File.Exists(CacheFile))
                    _snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(CacheFile)) ?? new();
            }
            catch (Exception e)
            {
                Glamourer.Log.Error($"Could not load wardrobe cache: {e}");
            }
        }

        var now = Environment.TickCount64;
        if (character == 0 || now < _nextScan)
            return;
        _nextScan = now + 500;

        var manager = MirageManager.Instance();
        if (manager != null && manager->PrismBoxLoaded)
        {
            var contents = new HashSet<uint>();
            var slots = manager->PrismBoxItemIds;
            for (var i = 0; i < slots.Length; ++i)
            {
                var id = slots[i] % 1_000_000;
                if (id == 0)
                    continue;
                if (!_outfits.TryGetValue(id, out var pieces))
                    contents.Add(id);
                else
                    for (var p = 0; p < pieces.Length; ++p)
                        if (pieces[p] != 0 && manager->IsSetSlotUnlocked((uint)i, p))
                            contents.Add(pieces[p]);
            }

            if (_snapshot.Dresser?.SetEquals(contents) != true)
            {
                _snapshot = _snapshot with { Dresser = contents };
                _dirty = true;
            }
        }

        var ui = UIState.Instance();
        if (ui != null && ui->Cabinet.IsCabinetLoaded())
        {
            var contents = new HashSet<uint>();
            foreach (var (row, item) in _cabinet)
                if (ui->Cabinet.IsItemInCabinet(row))
                    contents.Add(item);
            if (_snapshot.Armoire?.SetEquals(contents) != true)
            {
                _snapshot = _snapshot with { Armoire = contents };
                _dirty = true;
            }
        }
        Save();
    }

    private void Save()
    {
        if (!_dirty)
            return;
        try
        {
            File.WriteAllText(CacheFile + ".tmp", JsonSerializer.Serialize(_snapshot));
            File.Move(CacheFile + ".tmp", CacheFile, true);
            _dirty = false;
        }
        catch (Exception e)
        {
            Glamourer.Log.Error($"Could not save wardrobe cache: {e}");
        }
    }

    public void Dispose()
    {
        _framework.Update -= Update;
        Save();
    }
}
