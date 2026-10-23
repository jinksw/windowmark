using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WindowMark.Core;

internal sealed class LabelStore
{
    private sealed class StoreFile
    {
        public List<LabelRule> Rules { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowMark");

    public static string FilePath { get; } = Path.Combine(DataDirectory, "labels.json");

    private readonly List<LabelRule> _rules;

    private LabelStore(List<LabelRule> rules)
    {
        _rules = rules;
    }

    public IReadOnlyList<LabelRule> Rules => _rules;

    public static LabelStore Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new LabelStore(new List<LabelRule>());

            string json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions);
            var rules = data?.Rules?
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Label))
                .Where(rule => !string.IsNullOrWhiteSpace(rule.ExePath) || !string.IsNullOrWhiteSpace(rule.ClassName))
                .GroupBy(rule => IdentityKey(rule), StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList() ?? new List<LabelRule>();

            foreach (var rule in rules) rule.SanitizeLayout();
            return new LabelStore(rules);
        }
        catch (Exception ex)
        {
            Log.Error("读取标签数据失败", ex);
            try
            {
                if (File.Exists(FilePath))
                    File.Copy(FilePath, FilePath + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch
            {
            }
            return new LabelStore(new List<LabelRule>());
        }
    }

    public LabelRule? Find(TargetWindow target) => _rules.FirstOrDefault(rule => rule.Matches(target));

    public void Upsert(LabelRule rule)
    {
        _rules.RemoveAll(existing => existing.SameIdentity(rule));
        _rules.Add(rule.Clone());
        Save();
    }

    public void Remove(LabelRule rule)
    {
        if (_rules.RemoveAll(existing => existing.SameIdentity(rule)) > 0) Save();
    }

    private static string IdentityKey(LabelRule rule)
        => rule.ExePath.ToLowerInvariant() + "\u0001" + rule.ClassName + "\u0001" + rule.NativeTitle;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string json = JsonSerializer.Serialize(new StoreFile { Rules = _rules }, JsonOptions);
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Error("保存标签数据失败", ex);
        }
    }
}
