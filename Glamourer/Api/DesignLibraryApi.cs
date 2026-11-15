using System.Text.Json;
using Glamourer.Designs;
using Glamourer.Events;
using Luna;

namespace Glamourer.Api;

// Operate on the same filesystem and design objects as Glamourer's own selector.
public sealed class DesignLibraryApi : IApiService, IDisposable
{
    private readonly DesignManager designs;
    private readonly DesignFileSystem files;
    private readonly DesignChanged changed;
    private string? snapshot;

    public DesignLibraryApi(DesignManager designs, DesignFileSystem files, DesignChanged changed)
    {
        (this.designs, this.files, this.changed) = (designs, files, changed);
        files.Changed.Subscribe(OnFolderChange, 0);
        changed.Subscribe(OnDesignChange, DesignChanged.Priority.DesignFileSystem);
    }
    private void OnFolderChange(in FileSystemChanged.Arguments _) => snapshot = null;
    private void OnDesignChange(in DesignChanged.Arguments _) => snapshot = null;
    public void Dispose()
    {
        files.Changed.Unsubscribe(OnFolderChange);
        changed.Unsubscribe(OnDesignChange);
    }

    public string Snapshot() => snapshot ??= JsonSerializer.Serialize(new {
        Folders = files.Root.GetDescendants().OfType<IFileSystemFolder>().Select(f => f.FullPath),
        Designs = designs.Designs.Select(d => new { Id = d.Identifier, d.Name, d.Tags,
            Folder = d.Node?.Parent?.FullPath ?? string.Empty, d.LastEdit })
    });

    private IFileSystemFolder Folder(string path) => path.Length == 0 ? files.Root :
        files.Find(path, out var node) && node is IFileSystemFolder f ? f :
        throw new ArgumentException("The destination folder no longer exists.");

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("Enter a name without path separators.");
    }

    private static string Change(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception ex) { Glamourer.Log.Warning($"Design library operation failed: {ex}"); return ex.Message; }
    }

    public string CreateFolder(string parent, string name) => Change(() => {
        ValidateName(name);
        files.CreateFolder(Folder(parent), name.Trim());
    });

    public string CreateDesign(Guid id, string name, string parent, string[] tags) => Change(() => {
        ValidateName(name);
        var folder = Folder(parent);
        var fullName = folder.IsRoot ? name : $"{folder.FullPath}/{name}";
        designs.CreateEmpty(fullName, true, id, tags.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    });

    public string MoveDesign(Guid id, string parent) => Change(() => {
        var node = designs.Designs.ByIdentifier(id)?.Node ?? throw new ArgumentException("Design no longer exists.");
        if (node.Locked) throw new InvalidOperationException("Unlock the design in the Glamourer selector to move it.");
        files.Move(node, Folder(parent));
    });

    public string SetTag(Guid id, string tag, bool add) => Change(() => {
        var design = designs.Designs.ByIdentifier(id) ?? throw new ArgumentException("Design no longer exists.");
        if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Enter a tag.");
        if (design.WriteProtected()) throw new InvalidOperationException("Unlock the design in Glamourer to edit its tags.");
        var idx = Array.FindIndex(design.Tags, t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (add && idx < 0) designs.AddTag(design, tag);
        if (!add && idx >= 0) designs.RemoveTag(design, idx);
    });
}
