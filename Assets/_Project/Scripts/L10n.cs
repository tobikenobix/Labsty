using UnityEngine.Localization.Settings;

namespace ChemLab
{
    /// <summary>
    /// Thin wrapper over Unity Localization so call sites stay one short call.
    /// Tables: "UI" (interface text, keyed like "lobby.start") and "Hazards" (hazards.json text, keyed like "H01.title",
    /// "H01.Q2.question", "H01.Q2.optionA"). Edit them in Window > Asset Management > Localization Tables.
    /// </summary>
    public static class L10n
    {
        public const string UiTable = "UI", HazardTable = "Hazards";

        /// <summary>UI text in the selected language; args fill {0}, {1}... (string.Format rules).</summary>
        public static string T(string key, params object[] args) =>
            LocalizationSettings.StringDatabase.GetLocalizedString(UiTable, key, args);

        /// <summary>UI text, or `fallback` when the table has no such key (e.g. decoy names added to a scene later).</summary>
        public static string TOr(string key, string fallback) => Lookup(UiTable, key) ?? fallback;

        /// <summary>One hazards.json text field in the selected language; the English JSON text if the table has no entry.</summary>
        public static string Hazard(string key, string english) => string.IsNullOrEmpty(english) ? english : Lookup(HazardTable, key) ?? english;

        static string Lookup(string table, string key)
        {
            var entry = LocalizationSettings.StringDatabase.GetTable(table)?.GetEntry(key);
            if (entry != null && string.IsNullOrEmpty(entry.Value))   // empty here: ask the package, which applies the fallback locale
                return LocalizationSettings.StringDatabase.GetLocalizedString(table, key);
            return entry?.GetLocalizedString();
        }

        /// <summary>"EN", "DE", "FR", "IT" for the selected locale (used by the lobby buttons and the run export).</summary>
        public static string Code => (LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en").ToUpperInvariant();

        public static void Set(string code) =>
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code.ToLowerInvariant());

        /// <summary>Lower-cases a term for use mid-sentence, except in German where nouns keep their capital.</summary>
        public static string Lower(string s) => Code == "DE" ? s : s.ToLowerInvariant();

        /// <summary>Lower-cases only the first letter (an answer quoted mid-sentence); German keeps it.</summary>
        public static string LowerFirst(string s) => string.IsNullOrEmpty(s) || Code == "DE" ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
