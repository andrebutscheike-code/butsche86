using System.IO;
using System.Text.Json;

namespace Seitenwerk.Werkzeuge;

/// <summary>Benutzereinstellungen in %APPDATA%\Seitenwerk\einstellungen.json.</summary>
public sealed class Einstellungen
{
    private const int MaximumZuletzt = 10;

    public string? LibreOfficePfad { get; set; }
    public List<string> Zuletzt { get; set; } = new();
    public bool Rechtschreibung { get; set; } = true;
    public bool ZeigeLineale { get; set; } = true;
    public bool ZeigeNavigation { get; set; } = true;
    public bool ObjektFang { get; set; } = true;

    private static string Datei => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Seitenwerk", "einstellungen.json");

    public static Einstellungen Laden()
    {
        try
        {
            if (File.Exists(Datei))
                return JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(Datei)) ?? new Einstellungen();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Beschädigte oder gesperrte Einstellungen werden durch Standardwerte ersetzt.
        }
        return new Einstellungen();
    }

    public void Speichern()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Datei)!);
            File.WriteAllText(Datei, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind nicht kritisch.
        }
    }

    public void MerkeZuletzt(string pfad)
    {
        Zuletzt.RemoveAll(p => string.Equals(p, pfad, StringComparison.OrdinalIgnoreCase));
        Zuletzt.Insert(0, pfad);
        if (Zuletzt.Count > MaximumZuletzt)
            Zuletzt.RemoveRange(MaximumZuletzt, Zuletzt.Count - MaximumZuletzt);
    }
}
