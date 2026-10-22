namespace TikzEdt.Modern;

internal readonly record struct CanvasSelectionKey(int Offset, int Length, string Kind);

internal sealed class CanvasSelection
{
    public HashSet<CanvasSelectionKey> Keys { get; } = [];

    public bool Contains(CanvasSelectionKey key) => Keys.Contains(key) || Keys.Any(owner =>
        owner.Kind != "point" && owner.Offset <= key.Offset &&
        owner.Offset + owner.Length >= key.Offset + key.Length);

    // A node body and its coordinate marker are different hit targets for the same selection.
    public bool IncludesVisual(CanvasSelectionKey key) => Contains(key) || key.Kind != "point" && Keys.Any(member =>
        key.Offset <= member.Offset && key.Offset + key.Length >= member.Offset + member.Length);

    public bool Click(CanvasSelectionKey key, bool toggle)
    {
        if (toggle)
        {
            if (!Keys.Add(key)) { Keys.Remove(key); return false; }
        }
        else if (!IncludesVisual(key))
        {
            Keys.Clear();
            Keys.Add(key);
        }
        return true;
    }

    public static IReadOnlyList<CanvasSelectionKey> Outermost(IEnumerable<CanvasSelectionKey> keys)
    {
        var distinct = keys.GroupBy(key => (key.Offset, key.Length))
            .Select(group => group.OrderBy(key => key.Kind == "point" ? 1 : 0).First()).ToArray();
        return distinct.Where(key => !distinct.Any(other => other != key &&
            other.Offset <= key.Offset && other.Offset + other.Length >= key.Offset + key.Length &&
            (other.Offset != key.Offset || other.Length > key.Length)))
            .OrderBy(key => key.Offset).ToArray();
    }
}
