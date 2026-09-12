using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AutoPilotInput.Core;

namespace AutoPilotInput;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public List<AutomationAction> Actions { get; set; } = [];
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "Dark";
    public string Accent { get; set; } = "#9B8CFF";
    public string Effects { get; set; } = "Standard";
    public double Opacity { get; set; } = 1;
    public bool Sound { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public string StartHotkey { get; set; } = "F8";
    public string StopHotkey { get; set; } = "F9";
    public string CaptureHotkey { get; set; } = "F6";
    public string GitHubRepository { get; set; } = "Yunis3056/KeyTempo";
    public string TargetErrorPolicy { get; set; } = "PauseAction";
    public string InputErrorPolicy { get; set; } = "PauseAll";
    public string PointErrorPolicy { get; set; } = "Skip";
}

/// <summary>Only configuration is persisted; importing or loading never starts input.</summary>
public static class SettingsStore
{
    public const long MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    public static string ConfigPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPilotInput", "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static AppSettings Load(out string? warning)
    {
        lock (Gate)
        {
            warning = null;
            if (!File.Exists(ConfigPath)) return new AppSettings();
            try { return Read(ConfigPath); }
            catch (Exception ex) when (IsConfigurationError(ex))
            {
                var backup = ConfigPath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    File.Move(ConfigPath, backup);
                    warning = $"原配置无法读取，已保存在 {backup}。已恢复初始配置。 / Could not load configuration; original saved to {backup}. Defaults restored. {ex.Message}";
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    warning = $"配置无法读取，且无法备份。原文件仍位于 {ConfigPath}。 / Could not read or back up configuration; original remains at {ConfigPath}. {ex.Message} {backupError.Message}";
                }
                return new AppSettings();
            }
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate) WriteAtomic(ConfigPath, settings);
    }

    public static AppSettings Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(Path.GetFullPath(path));
    }

    public static void Export(string path, AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (Gate) WriteAtomic(Path.GetFullPath(path), settings);
    }

    public static void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Require(settings.SchemaVersion == 1, "不支持此配置版本。 / Unsupported configuration version.");
        Require(settings.Language is "zh-CN" or "en-US", "语言必须为 zh-CN 或 en-US。 / Invalid language.");
        Require(settings.Theme is "Dark" or "Light", "主题必须为 Dark 或 Light。 / Invalid theme.");
        Require(settings.Effects is "PowerSaving" or "Standard" or "Fancy", "动效档位无效。 / Invalid effects mode.");
        Require(settings.Accent is not null && Regex.IsMatch(settings.Accent, "^#[0-9a-fA-F]{6}$"), "强调色必须为 #RRGGBB。 / Accent must use #RRGGBB.");
        Require(double.IsFinite(settings.Opacity) && settings.Opacity is >= 0.5 and <= 1, "透明度必须在 0.5–1 之间。 / Opacity must be between 0.5 and 1.");
        Require(settings.GitHubRepository is not null && (settings.GitHubRepository.Length == 0 || Regex.IsMatch(settings.GitHubRepository, "^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")), "GitHub 仓库格式必须为 owner/repository。 / Repository must use owner/repository.");
        var gestures = new HashSet<(uint, uint)>();
        foreach (var hotkey in new[] { settings.StartHotkey, settings.StopHotkey, settings.CaptureHotkey, "F7" })
        {
            Require(HotkeyService.TryParse(hotkey, out var modifiers, out var key, out var error), error ?? "快捷键无效。 / Invalid hotkey.");
            Require(gestures.Add((modifiers, key)), "全局快捷键不能相同，F7 已保留用于结束采集。 / Global hotkeys must be different; F7 is reserved to finish capture.");
        }
        foreach (var policy in new[] { settings.TargetErrorPolicy, settings.InputErrorPolicy, settings.PointErrorPolicy })
            Require(policy is "PauseAction" or "PauseAll" or "Skip" or "SkipCycle", "异常处理方式无效。 / Invalid error policy.");
        Require(settings.Actions is not null && settings.Actions.Count <= 100, "配置最多包含 100 个动作。 / At most 100 actions are supported.");
        var actionIds = new HashSet<Guid>();
        foreach (var action in settings.Actions!)
        {
            Require(action is not null, "动作不能为 null。 / An action cannot be null.");
            Require(action!.Id != Guid.Empty && actionIds.Add(action.Id), "动作标识无效或重复。 / Action identifiers must be unique and nonempty.");
            Require(action.TargetTitle is not null && action.TargetTitle.Length <= 1024, "窗口名称过长。 / Window title is too long.");
            var actionErrors = NativeInput.ValidateAction(action, requireInput: false);
            Require(actionErrors.Count == 0, string.Join("\n", actionErrors));
        }
    }

    private static AppSettings Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 and <= MaximumFileBytes, "配置文件为空或超过 2 MB。 / Configuration is empty or exceeds 2 MB.");
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? throw new InvalidDataException("配置不能为空。 / Configuration cannot be null.");
        Validate(settings);
        return settings;
    }

    private static void WriteAtomic(string path, AppSettings settings)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        Require(bytes.LongLength <= MaximumFileBytes, "配置超过 2 MB。 / Configuration exceeds 2 MB.");
        var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static bool IsConfigurationError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
}
