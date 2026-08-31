namespace GbxSizeTree.Actions;

/// <summary>
/// Ordered action library shared by all frontends. Population happens once at startup
/// (Program.cs / test setup); the registry itself is immutable after construction.
/// </summary>
public sealed class ActionRegistry
{
    private readonly List<IMapAction> actions;

    public ActionRegistry(IEnumerable<IMapAction> actions)
    {
        this.actions = [.. actions.OrderBy(a => a.Order)];
        var dup = this.actions.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new ArgumentException($"Duplicate action id: {dup.Key}");
        }
    }

    public IReadOnlyList<IMapAction> All => actions;

    public IMapAction? Find(string id) => actions.FirstOrDefault(a => a.Id == id);

    public IEnumerable<IMapAction> Defaults() => actions.Where(a => a.DefaultOn);

    /// <summary>Applyable actions (excludes EditorOnly), optionally revealing experimental ones.</summary>
    public IEnumerable<IMapAction> Applyable(bool includeExperimental) =>
        actions.Where(a => a.Tier != ActionTier.EditorOnly
            && (includeExperimental || a.Tier != ActionTier.Experimental));
}
