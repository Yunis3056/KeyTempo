using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoPilotInput.Core;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 24
    };
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPilotInput", "config.json");
    public static AppConfig Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(ConfigPath)) return new AppConfig();
        try { return Import(ConfigPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            var backup = ConfigPath + $".invalid-{DateTime.Now:yyyyMMdd-HHmmssfff}";
            try { File.Copy(ConfigPath, backup, false); warning = $"配置无法读取，原文件已备份到 {backup}。{error.Message}"; }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { warning = $"配置无法读取，且备份失败：{error.Message} / {copyError.Message}"; }
            return new AppConfig();
        }
    }
    public static void Save(AppConfig config) => Export(ConfigPath, config);
    public static AppConfig Import(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 2_000_000) throw new InvalidDataException("配置文件超过 2 MB 限制。");
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("配置文件为空。");
        Validate(config);
        foreach (var action in config.Actions) { action.TargetHandle = 0; action.TargetProcessId = 0; }
        return config;
    }
    public static void Export(string path, AppConfig config)
    {
        Validate(config);
        var absolute = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(absolute)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, config, Options);
                stream.Flush(true);
            }
            if (File.Exists(absolute)) File.Replace(temp, absolute, null);
            else File.Move(temp, absolute);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Validate(AppConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException("配置版本不受支持。");
        if (config.Actions is null || config.Actions.Count > 100 || config.Actions.Any(action => action is null)) throw new InvalidDataException("动作列表无效（最多 100 个）。");
        if (config.Actions.Any(action => action.Id == Guid.Empty) || config.Actions.Select(action => action.Id).Distinct().Count() != config.Actions.Count)
            throw new InvalidDataException("动作标识无效或重复。");
        foreach (var action in config.Actions)
        {
            var errors = NativeInput.ValidateAction(action, requireInput: false);
            if (errors.Count > 0) throw new InvalidDataException($"{action.Name}: {string.Join(" ", errors)}");
            if (action.TargetTitle is null || action.TargetTitle.Length > 2048) throw new InvalidDataException("窗口标题无效。");
        }
        var settings = config.Settings ?? throw new InvalidDataException("设置不能为空。");
        if (settings.Theme is not ("Dark" or "Light") || settings.Animation is not ("Eco" or "Standard" or "Cool") || settings.Language is not ("zh-CN" or "en-US"))
            throw new InvalidDataException("主题、动效或语言设置无效。");
        if (!double.IsFinite(settings.Opacity) || settings.Opacity < .6 || settings.Opacity > 1) throw new InvalidDataException("窗口不透明度必须介于 60% 和 100% 之间。");
        if (settings.Accent is null || !Regex.IsMatch(settings.Accent, "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException("强调色必须为 #RRGGBB 格式。");
        if (!Enum.IsDefined(settings.WindowFailure) || !Enum.IsDefined(settings.InputFailure) || !Enum.IsDefined(settings.InvalidPoint)) throw new InvalidDataException("异常处理策略无效。");
        var hotkeys = new[] { settings.StartHotkey, settings.StopHotkey, settings.CaptureHotkey };
        if (hotkeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 64) || hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("三个全局快捷键必须有效且互不相同。");
        if (settings.Repository is null || (settings.Repository.Length > 0 && !Regex.IsMatch(settings.Repository, "^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")))
            throw new InvalidDataException("GitHub 仓库请使用 owner/repository 格式。");
    }
}
