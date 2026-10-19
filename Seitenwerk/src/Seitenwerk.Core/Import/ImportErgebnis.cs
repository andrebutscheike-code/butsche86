using Seitenwerk.Core.Modell;

namespace Seitenwerk.Core.Import;

public sealed class ImportErgebnis
{
    public ImportErgebnis(Dokument dokument, List<string> hinweise)
    {
        Dokument = dokument;
        Hinweise = hinweise;
    }

    public Dokument Dokument { get; }

    /// <summary>Hinweise auf Elemente, die nicht oder nur vereinfacht übernommen werden konnten.</summary>
    public List<string> Hinweise { get; }
}

public class ImportFehler : Exception
{
    public ImportFehler(string meldung, Exception? innere = null) : base(meldung, innere) { }
}
