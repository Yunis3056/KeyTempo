using System.IO;
using System.Text.Json;
using AutoPilotInput;
using AutoPilotInput.Core;
using Settings = AutoPilotInput.AppSettings;

// This suite checks configuration and parsing only. It never registers a global
// hotkey, sends desktop input, or touches the user's saved configuration.
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or JsonException) { checks++; return; }
    throw new Exception(name);
}

Check(HotkeyService.TryParse("Ctrl+Alt+F8", out var modifiers, out var key, out _) && modifiers == 3 && key == 0x77, "Ctrl+Alt+F8");
Check(HotkeyService.TryParse("Shift+1", out modifiers, out key, out _) && modifiers == 4 && key == 49, "Shift+1");
Check(HotkeyService.TryParse("Esc", out modifiers, out key, out _) && modifiers == 0 && key == 27, "Esc alias");
foreach (var invalid in new[] { "", "Ctrl+Ctrl+F8", "F8+F9", "Ctrl", "F8+", "None", "Alt+LeftCtrl", "garbage", "12" })
    Check(!HotkeyService.TryParse(invalid, out _, out _, out _), "Invalid gesture " + invalid);

var settings = new Settings();
SettingsStore.Validate(settings);
settings.Actions.Add(new AutomationAction { TargetHandle = 999, TargetProcessId = 321, Name = "Test", Keys = [] });
var directory = Path.Combine(Path.GetTempPath(), "AutoPilotInput.Settings.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "roundtrip.json");
try
{
    SettingsStore.Export(path, settings);
    Check(!File.ReadAllText(path).Contains("TargetHandle"), "No persisted handle");
    var imported = SettingsStore.Import(path);
    Check(imported.Actions[0].Keys.Count == 0 && imported.Actions[0].TargetHandle == 0 && imported.Actions[0].TargetProcessId == 0, "Blank draft and target identity reset");
    settings.Actions[0].Name = "Updated";
    SettingsStore.Export(path, settings);
    Check(SettingsStore.Import(path).Actions[0].Name == "Updated", "Atomic replacement");
    settings.Actions[0].IntervalSeconds = double.NaN;
    Reject(() => SettingsStore.Validate(settings), "NaN rejected");
    settings.Actions[0].IntervalSeconds = 604801;
    Reject(() => SettingsStore.Validate(settings), "Over seven days rejected");
    settings.Actions[0].IntervalSeconds = 300;
    settings.Actions[0].IntervalMode = IntervalMode.Jitter;
    settings.Actions[0].JitterSeconds = 301;
    Reject(() => SettingsStore.Validate(settings), "Negative jitter range rejected");
    settings.Actions[0].IntervalMode = IntervalMode.Fixed;
    settings.Actions.Add(settings.Actions[0]);
    Reject(() => SettingsStore.Validate(settings), "Duplicate IDs rejected");
    settings.Actions.RemoveAt(1);
    settings.Actions[0].Name = new string('a', 101);
    Reject(() => SettingsStore.Validate(settings), "Overlong action name rejected");
    settings.Actions[0].Name = "Test";
    settings.Actions[0].Points = Enumerable.Range(0, 101).Select(_ => new ClickPoint()).ToList();
    Reject(() => SettingsStore.Validate(settings), "Too many mouse points rejected");
    settings.Actions[0].Points = [];
    settings.Actions[0].Keys = Enumerable.Range(65, 9).ToList();
    Reject(() => SettingsStore.Validate(settings), "Too many keys rejected");
    settings.Actions[0].Keys = [];
    settings.StopHotkey = "F8";
    Reject(() => SettingsStore.Validate(settings), "Duplicate hotkeys rejected");
    settings.StopHotkey = "F7";
    Reject(() => SettingsStore.Validate(settings), "Reserved F7 rejected");
    settings.StopHotkey = "F9";
    SettingsStore.Validate(settings);
    File.WriteAllText(path, "{\"Actions\":null}");
    Reject(() => SettingsStore.Import(path), "Null actions rejected");
    File.WriteAllText(path, "{\"SchemaVersion\":2}");
    Reject(() => SettingsStore.Import(path), "Future schema rejected");
    File.WriteAllText(path, "{broken");
    Reject(() => SettingsStore.Import(path), "Malformed file rejected");
    File.WriteAllText(path, new string(' ', (int)SettingsStore.MaximumFileBytes + 1));
    Reject(() => SettingsStore.Import(path), "Oversized file rejected");
}
finally
{
    if (File.Exists(path)) File.Delete(path);
    Directory.Delete(directory);
}
Console.WriteLine($"PASS: {checks} settings and hotkey parser checks. No desktop input sent or hotkeys registered.");
