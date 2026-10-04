using System.Collections.ObjectModel;

namespace Vid2Tiny.Infrastructure;

public static class CollectionSync
{
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyCollection<T> desired) where T : class
    {
        var keep = desired.ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        var present = target.ToHashSet();
        foreach (var item in desired.Where(item => !present.Contains(item)))
        {
            target.Add(item);
        }
    }
}
