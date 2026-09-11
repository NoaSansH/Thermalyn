// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json;
using Thermalyn.Models;

namespace Thermalyn.Services;

public sealed class ThermalEventService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn");
    private static readonly string FilePath = Path.Combine(Folder, "events.json");
    private const int MaxEntries = 60;

    // Persisted tokens, never displayed: see Common.Hot and Common.Critical.
    public const string Hot = "Hot";
    public const string Critical = "Critical";

    public static bool IsCritical(string level) => level == Critical;

    public static string Describe(string level) =>
        LocalizationService.Get(IsCritical(level) ? "Common.Critical" : "Common.Hot");

    private readonly Dictionary<string, ThermalEvent> _open = [];
    private List<ThermalEvent> _entries = [];
    private bool _dirty;

    public IReadOnlyList<ThermalEvent> Entries => _entries;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            _entries = JsonSerializer.Deserialize<List<ThermalEvent>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch { _entries = []; }
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var staged = FilePath + ".tmp";
            File.WriteAllText(staged, JsonSerializer.Serialize(_entries, WriteOptions));
            File.Move(staged, FilePath, overwrite: true);
            _dirty = false;
        }
        catch { }
    }

    public void Clear()
    {
        _open.Clear();
        _entries.Clear();
        _dirty = false;
        try { File.Delete(FilePath); } catch { }
    }

    public bool Track(string component, double? temperature, int hotThreshold, int criticalThreshold)
    {
        if (temperature is not double value) return false;
        var level = value >= criticalThreshold ? Critical : value >= hotThreshold ? Hot : null;

        if (level is null)
        {
            if (!_open.Remove(component, out var closed)) return false;
            closed.End = DateTime.Now;
            _dirty = true;
            return true;
        }

        if (_open.TryGetValue(component, out var current))
        {
            var changed = false;
            if (value > current.Peak) { current.Peak = value; changed = true; }
            if (level == Critical && !IsCritical(current.Level)) { current.Level = level; changed = true; }
            current.End = DateTime.Now;
            _dirty |= changed;
            return changed;
        }

        var opened = new ThermalEvent { Component = component, Level = level, Start = DateTime.Now, End = DateTime.Now, Peak = value };
        _open[component] = opened;
        _entries.Insert(0, opened);
        while (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        _dirty = true;
        return true;
    }
}
