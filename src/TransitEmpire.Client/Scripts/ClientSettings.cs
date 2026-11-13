using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace TransitEmpire.Client.Scripts;

/// <summary>
/// Preferências persistentes do cliente (gráficos, VSync, escala HUD, áudio e onboarding) salvas em user://settings.json.
/// </summary>
public sealed class ClientSettingsData
{
    public string Preset { get; set; } = "Balanced";
    public bool SsaoEnabled { get; set; } = true;
    public bool GlowEnabled { get; set; } = true;
    public bool SsrEnabled { get; set; } = false;
    public bool TiltShiftEnabled { get; set; } = true;
    public bool Shadows4Splits { get; set; } = false;
    public bool DynamicSolarCycle { get; set; } = true;
    public bool VSyncEnabled { get; set; } = true;
    public float HudScale { get; set; } = 1.0f;
    public float MasterVolume { get; set; } = 0.85f;
    public float AmbientVolume { get; set; } = 0.28f;
    public bool AudioMuted { get; set; } = false;
    public bool AutoSaveEnabled { get; set; } = true;
    public int PreferredMapSize { get; set; } = 100;
    public string PreferredScenario { get; set; } = "Balanced";
    public bool PreferredHasRivers { get; set; } = true;
    public string PreferredMountains { get; set; } = "Normal";
    public bool TutorialCompleted { get; set; } = false;
    public bool TutorialHidden { get; set; } = false;
}

public static class ClientSettingsStore
{
    private const string SettingsUserPath = "user://settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ClientSettingsData Load()
    {
        try
        {
            string absPath = ProjectSettings.GlobalizePath(SettingsUserPath);
            if (!File.Exists(absPath))
                return new ClientSettingsData();

            string json = File.ReadAllText(absPath);
            var loaded = JsonSerializer.Deserialize<ClientSettingsData>(json, JsonOptions);
            if (loaded == null)
                return new ClientSettingsData();

            loaded.HudScale = Math.Clamp(loaded.HudScale, 0.85f, 1.25f);
            loaded.MasterVolume = Math.Clamp(loaded.MasterVolume, 0f, 1f);
            loaded.AmbientVolume = Math.Clamp(loaded.AmbientVolume, 0f, 1f);
            if (loaded.PreferredMapSize is not (50 or 80 or 100))
                loaded.PreferredMapSize = 100;
            return loaded;
        }
        catch
        {
            return new ClientSettingsData();
        }
    }

    public static void Save(ClientSettingsData data)
    {
        try
        {
            string absPath = ProjectSettings.GlobalizePath(SettingsUserPath);
            string? dir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(absPath, json);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Falha ao salvar settings.json: {ex.Message}");
        }
    }
}

