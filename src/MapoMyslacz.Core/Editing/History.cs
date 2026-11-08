namespace MapoMyslacz.Core.Editing;

/// <summary>
/// Historia cofania oparta na migawkach (zwarty JSON całej mapy). Proste i odporne — każdy krok to
/// pełny stan, więc cofnięcie nie może „rozjechać” mapy, a mapy myśli są na to wystarczająco małe.
/// </summary>
public sealed class History(int limit = 300)
{
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    /// <summary>Zapamiętuje stan sprzed zmiany i czyści „ponów”.</summary>
    public void Push(string before)
    {
        _undo.AddLast(before);
        while (_undo.Count > limit) _undo.RemoveFirst();
        _redo.Clear();
    }

    /// <summary>Zwraca stan do przywrócenia (albo null), zapamiętując bieżący do „ponów”.</summary>
    public string? Undo(string current)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _redo.Push(current);
        return last.Value;
    }

    public string? Redo(string current)
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(current);
        return _redo.Pop();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
