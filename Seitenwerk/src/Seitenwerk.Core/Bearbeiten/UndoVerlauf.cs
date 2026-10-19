namespace Seitenwerk.Core.Bearbeiten;

/// <summary>Rückgängig/Wiederholen über vollständige Momentaufnahmen des Dokuments.</summary>
public sealed class UndoVerlauf
{
    private readonly List<string> _staende = new();
    private readonly int _maximum;
    private int _index = -1;

    public UndoVerlauf(int maximum = 200) => _maximum = Math.Max(2, maximum);

    public bool KannRueckgaengig => _index > 0;
    public bool KannWiederholen => _index >= 0 && _index < _staende.Count - 1;

    public void Zuruecksetzen(string stand)
    {
        _staende.Clear();
        _staende.Add(stand);
        _index = 0;
    }

    /// <summary>Merkt einen neuen Stand. Gibt false zurück, wenn er dem aktuellen entspricht.</summary>
    public bool Merke(string stand)
    {
        if (_index >= 0 && _staende[_index] == stand)
            return false;
        // Alles hinter dem aktuellen Stand verfällt, sobald etwas Neues passiert.
        if (_index < _staende.Count - 1)
            _staende.RemoveRange(_index + 1, _staende.Count - _index - 1);
        _staende.Add(stand);
        _index = _staende.Count - 1;
        if (_staende.Count > _maximum)
        {
            _staende.RemoveAt(0);
            _index--;
        }
        return true;
    }

    public string? Rueckgaengig() => KannRueckgaengig ? _staende[--_index] : null;

    public string? Wiederholen() => KannWiederholen ? _staende[++_index] : null;
}
