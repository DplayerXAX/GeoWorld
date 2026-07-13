#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor tool: rebuild a LevelDefinition's tutorialSteps list from a CSV table
// (author it in Excel — File ▸ Save As ▸ CSV UTF-8 — or Google Sheets ▸
// File ▸ Download ▸ CSV) instead of clicking through the Inspector's list one
// field at a time.
//
//   1. Select the target LevelDefinition asset in the Project window.
//   2. GeoWorld ▸ Tutorial ▸ Import Steps from CSV...
//   3. Pick your .csv file.
//
// One row = one TutorialStep. First row must be the header (column names,
// order doesn't matter, case-insensitive). See
// Assets/Scripts/Meta/Editor/Tutorial_Template_1-1.csv for the exact columns
// and a filled-in example (Level_1's current tutorial, as a starting point).
//
// Every column except "Kind" is optional — a blank cell falls back to the
// TutorialStep field's own default. Re-running the import REPLACES the whole
// tutorialSteps list (it doesn't merge), so keep the CSV as the source of
// truth once you start using it.
public static class TutorialCsvImporter
{
    [MenuItem("GeoWorld/Tutorial/Import Steps from CSV...")]
    static void ImportMenu()
    {
        var lv = Selection.activeObject as LevelDefinition;
        if (lv == null)
        {
            EditorUtility.DisplayDialog("Import Tutorial CSV",
                "Select a LevelDefinition asset in the Project window first, then run this again.", "OK");
            return;
        }

        string path = EditorUtility.OpenFilePanel("Import Tutorial Steps CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var steps = Parse(ReadTextShared(path));
            Undo.RecordObject(lv, "Import Tutorial Steps");
            lv.tutorialSteps = steps;
            EditorUtility.SetDirty(lv);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TutorialCsvImporter] Imported {steps.Count} step(s) into '{lv.name}' from {path}");
            EditorGUIUtility.PingObject(lv);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Import Tutorial CSV", $"Failed: {e.Message}", "OK");
            Debug.LogException(e);
        }
    }

    // File.ReadAllText opens with exclusive access and throws IOException
    // ("Sharing violation") if the CSV is still open in Excel/Sheets-sync/etc.
    // Open it explicitly allowing other readers/writers instead — the common
    // workflow is editing the CSV live and re-importing without closing it.
    static string ReadTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static List<TutorialStep> Parse(string csvText)
    {
        var rows = ParseCsv(csvText);
        if (rows.Count == 0) return new List<TutorialStep>();

        var header = rows[0];
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (!string.IsNullOrEmpty(name)) col[name] = i;
        }

        string Get(List<string> row, string name) =>
            col.TryGetValue(name, out int i) && i < row.Count ? row[i].Trim() : "";

        var blockCache = BuildAssetCache<BlockData>();
        var charCache  = BuildAssetCache<DialogueCharacter>();
        var convoCache = BuildAssetCache<DialogueConversation>();

        var steps = new List<TutorialStep>();
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == 0 || row.TrueForAll(string.IsNullOrWhiteSpace)) continue;   // blank row → skip

            string kindStr = Get(row, "Kind");
            if (string.IsNullOrEmpty(kindStr)) continue;   // no kind → not a real step row
            if (!Enum.TryParse(kindStr, true, out TutorialStepKind kind))
                throw new Exception($"Row {r + 1}: unknown Kind '{kindStr}'.");

            var step = new TutorialStep { kind = kind };

            step.block = ResolveAsset(blockCache, Get(row, "Block"));
            step.origin = new Vector3Int(
                ParseInt(Get(row, "OriginX")), ParseInt(Get(row, "OriginY")), ParseInt(Get(row, "OriginZ")));
            step.rotation90 = new Vector3Int(
                ParseInt(Get(row, "RotX")), ParseInt(Get(row, "RotY")), ParseInt(Get(row, "RotZ")));
            step.cellsOverride = ParseCells(Get(row, "CellsOverride"));

            step.waitSeconds    = ParseFloat(Get(row, "WaitSeconds"));
            step.count          = ParseInt(Get(row, "Count"));
            step.inputKey       = ParseKeyCode(Get(row, "InputKey"));
            step.pathLength     = ParseInt(Get(row, "PathLength"));
            step.freeOperations = ParseBool(Get(row, "FreeOperations"));
            step.hideInCombat   = ParseBool(Get(row, "HideInCombat"));
            step.requiredWave   = ParseInt(Get(row, "RequiredWave"));
            step.unlocksOps     = ParseOpsList(Get(row, "UnlocksOps"));

            step.cameraFocus = ParseEnum(Get(row, "CameraFocus"), TutorialFocus.None);
            step.focusZoom   = ParseFloat(Get(row, "FocusZoom"));

            step.hint             = Get(row, "Hint");
            step.conversation     = ResolveAsset(convoCache, Get(row, "Conversation"));
            step.speaker          = ResolveAsset(charCache, Get(row, "Speaker"));
            step.speakerSlot      = ParseEnum(Get(row, "SpeakerSlot"), PortraitSlot.Left);
            step.speakerPortrait  = Get(row, "SpeakerPortrait");

            steps.Add(step);
        }
        return steps;
    }

    // ── Field parsers ─────────────────────────────────────────────────────────
    static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static float ParseFloat(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    static bool ParseBool(string s) =>
        s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);

    static T ParseEnum<T>(string s, T fallback) where T : struct =>
        !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out T v) ? v : fallback;

    static KeyCode ParseKeyCode(string s) =>
        !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out KeyCode v) ? v : KeyCode.Mouse0;

    // "x,y,z|x,y,z|..." — only needed for the rare Custom/cellsOverride shape.
    static Vector3Int[] ParseCells(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<Vector3Int>();
        var parts = s.Split('|');
        var cells = new Vector3Int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var xyz = parts[i].Split(',');
            cells[i] = new Vector3Int(
                xyz.Length > 0 ? ParseInt(xyz[0]) : 0,
                xyz.Length > 1 ? ParseInt(xyz[1]) : 0,
                xyz.Length > 2 ? ParseInt(xyz[2]) : 0);
        }
        return cells;
    }

    // "Sell;Upgrade" → unlocksOps entries. Blank → null (no permanent unlocks).
    static TutorialStepKind[] ParseOpsList(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<TutorialStepKind>();
        foreach (var p in parts)
            if (Enum.TryParse(p.Trim(), true, out TutorialStepKind k)) list.Add(k);
        return list.Count > 0 ? list.ToArray() : null;
    }

    // ── Asset name lookup (BlockData / DialogueCharacter / DialogueConversation) ─
    static Dictionary<string, T> BuildAssetCache<T>() where T : UnityEngine.Object
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var obj  = AssetDatabase.LoadAssetAtPath<T>(path);
            if (obj != null && !map.ContainsKey(obj.name)) map[obj.name] = obj;
        }
        return map;
    }

    static T ResolveAsset<T>(Dictionary<string, T> cache, string name) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (cache.TryGetValue(name, out var obj)) return obj;
        Debug.LogWarning($"[TutorialCsvImporter] Couldn't find {typeof(T).Name} named '{name}' — left blank.");
        return null;
    }

    // ── Minimal RFC4180-ish CSV parser ───────────────────────────────────────
    // Handles quoted fields, embedded commas/newlines inside quotes, and ""
    // as an escaped quote — exactly what Excel/Sheets export.
    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add(row); row = new List<string>(); }

        while (i < n)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }
            if (c == '"') { inQuotes = true; i++; continue; }
            if (c == ',') { EndField(); i++; continue; }
            if (c == '\r') { i++; continue; }   // normalize CRLF/CR → \n only
            if (c == '\n') { EndRow(); i++; continue; }
            field.Append(c); i++;
        }
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }
}
#endif
