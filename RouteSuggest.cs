using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RouteSuggest;

/// <summary>
/// Configuration for a path type including scoring weights, color, and priority.
/// </summary>
public class PathConfig
{
    /// <summary>
    /// Name for this path configuration (e.g., "Safe", "Aggressive").
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Color used to highlight this path type on the map.
    /// </summary>
    public Color Color { get; set; }

    /// <summary>
    /// Higher priority paths are rendered on top of lower priority paths when they overlap.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Scoring weights for each room type. Positive values make the room more desirable,
    /// negative values make it less desirable. Only rooms with defined weights contribute to the score.
    /// </summary>
    public Dictionary<MapPointType, int> ScoringWeights { get; set; } = new Dictionary<MapPointType, int>();

    /// <summary>
    /// Calculates the total score for a given path using this configuration's scoring weights.
    /// </summary>
    /// <param name="path">List of map points representing the path to score.</param>
    /// <returns>The total score, or 0 if path is null or empty.</returns>
    public int CalculateScore(List<MapPoint> path)
    {
        if (path == null) return 0;

        int score = 0;
        foreach (var point in path)
        {
            if (ScoringWeights.TryGetValue(point.PointType, out int weight))
            {
                score += weight;
            }
        }
        return score;
    }
}

/// <summary>
/// Main mod class for RouteSuggest. Provides optimal path suggestions on the map with multiple strategies.
/// </summary>
[ModInitializer("ModLoaded")]
public static class RouteSuggest
{
    /// <summary>
    /// Current run state, set when a run starts.
    /// </summary>
    public static RunState RunState { get; private set; }

    /// <summary>
    /// Dictionary of calculated paths for each configured path type.
    /// Key is the path config name, value is the calculated path.
    /// </summary>
    public static Dictionary<string, List<MapPoint>> CalculatedPaths { get; private set; } = new Dictionary<string, List<MapPoint>>();

    /// <summary>
    /// List of configured path types. Can be modified at runtime or loaded from config file.
    /// Defaults contain Safe (gold) and Aggressive (red) path types.
    /// </summary>
    public static List<PathConfig> PathConfigs { get; private set; }

    /// <summary>
    /// Default path configurations used as fallback and for reset functionality.
    /// </summary>
    private static readonly List<PathConfig> DefaultPathConfigs = new List<PathConfig>
    {
        new PathConfig
        {
            Name = "Safe",
            Color = new Color(1f, 0.84f, 0f, 1f), // Gold
            Priority = 100, // Higher priority - shown over aggressive path
            ScoringWeights = new Dictionary<MapPointType, int>
            {
                { MapPointType.RestSite, 1 },
                { MapPointType.Treasure, 1 },
                { MapPointType.Shop, 1 },
                { MapPointType.Monster, -1 },
                { MapPointType.Elite, -3 },
                { MapPointType.Unknown, 0 }
            }
        },
        new PathConfig
        {
            Name = "Aggressive",
            Color = new Color(1f, 0f, 0f, 1f), // Red
            Priority = 50, // Lower priority - safe path shown over this
            ScoringWeights = new Dictionary<MapPointType, int>
            {
                { MapPointType.RestSite, 1 },
                { MapPointType.Treasure, 1 },
                { MapPointType.Shop, 1 },
                { MapPointType.Monster, 2 },
                { MapPointType.Elite, 3 },
                { MapPointType.Unknown, 2 }
            }
        }
    };

    /// <summary>
    /// Stores original colors and scales for each highlighted TextureRect to restore later.
    /// </summary>
    private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTickProperties = new Dictionary<TextureRect, (Color, Vector2)>();

    /// <summary>
    /// Cached reflection field for accessing the map screen's internal paths dictionary.
    /// </summary>
    private static FieldInfo _pathsField;

    /// <summary>
    /// Whether reflection has been successfully initialized.
    /// </summary>
    private static bool _reflectionInitialized = false;

    /// <summary>
    /// Flag indicating if a highlight request is pending for when the map screen opens.
    /// </summary>
    private static bool _pendingHighlight = false;

    /// <summary>
    /// Called when the mod is loaded. Initializes configuration, subscribes to game events,
    /// and sets up reflection for map highlighting.
    /// </summary>
    public static void ModLoaded()
    {
        Log.Warn("RouteSuggest: Mod loaded");

        // Initialize PathConfigs from defaults
        PathConfigs = new List<PathConfig>();
        ResetToDefaultPathConfigs();

        // Load configuration from file if available
        LoadConfig();

        // Pretty print the current path configurations
        PrintPathConfigs();

        // Subscribe to game events
        var manager = RunManager.Instance;
        manager.RunStarted += OnRunStarted;
        manager.ActEntered += OnActEntered;
        manager.RoomEntered += OnRoomEntered;
        manager.RoomExited += OnRoomExited;

        // Initialize reflection for path highlighting
        InitializeReflection();

        // Register with ModConfig for GUI settings (via reflection)
        DeferredRegisterModConfig();
    }

    /// <summary>
    /// Deferred registration with ModConfig to ensure all mods are loaded first.
    /// Uses reflection for zero-dependency integration.
    /// </summary>
    static void DeferredRegisterModConfig()
    {
        // Wait one frame for all mods to load
        var tree = (SceneTree)Engine.GetMainLoop();
        Action callback = null;
        callback = () =>
        {
            tree.ProcessFrame -= callback;
            RegisterModConfigViaReflection();
        };
        tree.ProcessFrame += callback;
    }

    /// <summary>
    /// Registers path configurations with ModConfig using reflection.
    /// This allows ModConfig integration without a hard DLL dependency.
    /// </summary>
    static void RegisterModConfigViaReflection()
    {
        try
        {
            // Check if ModConfig is available
            var apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
            var entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
            var configType = Type.GetType("ModConfig.ConfigType, ModConfig");

            if (apiType == null || entryType == null || configType == null)
            {
                Log.Warn("RouteSuggest: ModConfig not found, skipping GUI registration");
                return;
            }

            var entries = new List<object>();

            // Helper to create ConfigEntry via reflection
            object MakeEntry(string key, string label, object type,
                object defaultValue = null, float min = 0, float max = 100, float step = 1,
                string format = "F0", string[] options = null,
                Action<object> onChanged = null)
            {
                var entry = Activator.CreateInstance(entryType);
                entryType.GetProperty("Key")?.SetValue(entry, key);
                entryType.GetProperty("Label")?.SetValue(entry, label);
                entryType.GetProperty("Type")?.SetValue(entry, type);
                if (defaultValue != null)
                    entryType.GetProperty("DefaultValue")?.SetValue(entry, defaultValue);
                entryType.GetProperty("Min")?.SetValue(entry, min);
                entryType.GetProperty("Max")?.SetValue(entry, max);
                entryType.GetProperty("Step")?.SetValue(entry, step);
                entryType.GetProperty("Format")?.SetValue(entry, format);
                if (options != null)
                    entryType.GetProperty("Options")?.SetValue(entry, options);
                if (onChanged != null)
                    entryType.GetProperty("OnChanged")?.SetValue(entry, onChanged);
                return entry;
            }

            // Helper to get ConfigType enum value
            object GetConfigType(string name) => Enum.Parse(configType, name);

            // Path Management section at the top
            entries.Add(MakeEntry("", "Path Management", GetConfigType("Header")));

            // Slider to add new path (0->1 triggers add)
            entries.Add(MakeEntry("__add_path", "Add New Path (slide to 1)",
                GetConfigType("Slider"),
                defaultValue: 0f,
                min: 0, max: 1, step: 1, format: "F0",
                onChanged: (value) =>
                {
                    if ((int)(float)value == 1)
                    {
                        var newConfig = new PathConfig
                        {
                            Name = $"Path{PathConfigs.Count + 1}",
                            Color = new Color(1f, 1f, 1f, 1f),
                            Priority = 50,
                            ScoringWeights = new Dictionary<MapPointType, int>()
                        };
                        PathConfigs.Add(newConfig);
                        SaveConfiguration();

                        // Re-register to refresh UI
                        RegisterModConfigViaReflection();
                    }
                }));

            entries.Add(MakeEntry("", "", GetConfigType("Separator")));

            // Add configuration for each path
            for (int i = 0; i < PathConfigs.Count; i++)
            {
                var config = PathConfigs[i];
                var pathIndex = i;

                // Path header with remove slider
                entries.Add(MakeEntry("", $"Path {i + 1}: {config.Name}", GetConfigType("Header")));

                // Remove this path slider (0 = keep, 1 = remove)
                entries.Add(MakeEntry($"path_{i}_remove", "Remove Path (0=keep, 1=remove)",
                    GetConfigType("Slider"),
                    defaultValue: 0f,
                    min: 0, max: 1, step: 1, format: "F0",
                    onChanged: (value) =>
                    {
                        if ((int)(float)value == 1)
                        {
                            PathConfigs.RemoveAt(pathIndex);
                            SaveConfiguration();

                            // Re-register to refresh UI
                            RegisterModConfigViaReflection();
                        }
                    }));

                // Name
                entries.Add(MakeEntry($"path_{i}_name", "Name", GetConfigType("TextInput"),
                    defaultValue: config.Name,
                    onChanged: (value) =>
                    {
                        config.Name = (string)value;
                        SaveConfiguration();
                    }));

                // Color (hex input)
                entries.Add(MakeEntry($"path_{i}_color", "Color (hex, e.g., #FFD700)",
                    GetConfigType("TextInput"),
                    defaultValue: $"#{config.Color.ToHtml(false)}",
                    onChanged: (value) =>
                    {
                        config.Color = ParseColor((string)value);
                        SaveConfiguration();
                    }));

                // Priority
                entries.Add(MakeEntry($"path_{i}_priority", "Priority (higher = on top)",
                    GetConfigType("Slider"),
                    defaultValue: (float)config.Priority,
                    min: 0, max: 200, step: 10, format: "F0",
                    onChanged: (value) =>
                    {
                        config.Priority = (int)(float)value;
                        SaveConfiguration();
                    }));

                // Scoring weights section header
                entries.Add(MakeEntry("", "Scoring Weights", GetConfigType("Header")));

                // Add weights for each room type
                var roomTypes = new[] { MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop,
                    MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown, MapPointType.Boss };
                foreach (var roomType in roomTypes)
                {
                    if (!config.ScoringWeights.TryGetValue(roomType, out int weight))
                        weight = 0;

                    var capturedRoomType = roomType;
                    entries.Add(MakeEntry($"path_{i}_weight_{roomType}", roomType.ToString(),
                        GetConfigType("Slider"),
                        defaultValue: (float)weight,
                        min: -100, max: 100, step: 1, format: "F0",
                        onChanged: (value) =>
                        {
                            config.ScoringWeights[capturedRoomType] = (int)(float)value;
                            SaveConfiguration();
                        }));
                }

                entries.Add(MakeEntry("", "", GetConfigType("Separator")));
            }

            // Register with ModConfig via reflection
            var registerMethod = apiType.GetMethod("Register",
                new[] { typeof(string), typeof(string), entries.ToArray().GetType() });
            registerMethod?.Invoke(null, new object[] { "RouteSuggest", "RouteSuggest", entries.ToArray() });

            Log.Warn($"RouteSuggest: Registered {entries.Count()} entries with ModConfig (via reflection)");
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Failed to register with ModConfig: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current PathConfigs to RouteSuggestConfig.json.
    /// Called automatically when settings are changed via ModConfig.
    /// </summary>
    static void SaveConfiguration()
    {
        try
        {
            string executablePath = OS.GetExecutablePath();
            string directoryName = Path.GetDirectoryName(executablePath);
            string modsPath = Path.Combine(directoryName, "mods");
            string configPath = Path.Combine(modsPath, "RouteSuggestConfig.json");

            var configData = new ConfigFile
            {
                SchemaVersion = 1,
                PathConfigs = new List<PathConfigEntry>()
            };

            foreach (var config in PathConfigs)
            {
                var entry = new PathConfigEntry
                {
                    Name = config.Name,
                    Color = $"#{config.Color.ToHtml(false)}",
                    Priority = config.Priority,
                    ScoringWeights = new Dictionary<string, int>()
                };

                foreach (var weight in config.ScoringWeights)
                {
                    entry.ScoringWeights[weight.Key.ToString()] = weight.Value;
                }

                configData.PathConfigs.Add(entry);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            string json = JsonSerializer.Serialize(configData, options);
            File.WriteAllText(configPath, json);

            Log.Warn($"RouteSuggest: Configuration saved to {configPath}");
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Failed to save configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Resets PathConfigs to the default configuration.
    /// Creates deep copies of default configs to avoid modifying the originals.
    /// </summary>
    public static void ResetToDefaultPathConfigs()
    {
        PathConfigs.Clear();
        foreach (var defaultConfig in DefaultPathConfigs)
        {
            var config = new PathConfig
            {
                Name = defaultConfig.Name,
                Color = defaultConfig.Color,
                Priority = defaultConfig.Priority,
                ScoringWeights = new Dictionary<MapPointType, int>(defaultConfig.ScoringWeights)
            };
            PathConfigs.Add(config);
        }
        Log.Warn("RouteSuggest: Reset to default path configurations");
    }

    /// <summary>
    /// Loads path configurations from RouteSuggestConfig.json if it exists.
    /// Falls back to default PathConfigs if file is missing or invalid.
    /// </summary>
    static void LoadConfig()
    {
        try
        {
            string executablePath = OS.GetExecutablePath();
            string directoryName = Path.GetDirectoryName(executablePath);
            string modsPath = Path.Combine(directoryName, "mods");
            string configPath = Path.Combine(modsPath, "RouteSuggestConfig.json");

            if (!File.Exists(configPath))
            {
                Log.Warn($"RouteSuggest: Config file not found at {configPath}, using default path configs");
                return;
            }

            string json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var configData = JsonSerializer.Deserialize<ConfigFile>(json, options);

            if (configData?.SchemaVersion != 1)
            {
                Log.Warn($"RouteSuggest: Unsupported schema version {configData?.SchemaVersion}, using defaults");
                return;
            }

            if (configData.PathConfigs == null)
            {
                Log.Warn("RouteSuggest: No path configs found in config file, using defaults");
                return;
            }

            // Clear and replace with loaded configs
            PathConfigs.Clear();
            foreach (var configEntry in configData.PathConfigs)
            {
                var config = new PathConfig
                {
                    Name = configEntry.Name,
                    Priority = configEntry.Priority,
                    Color = ParseColor(configEntry.Color),
                    ScoringWeights = ParseScoringWeights(configEntry.ScoringWeights)
                };
                PathConfigs.Add(config);
                Log.Warn($"RouteSuggest: Loaded path config '{config.Name}' from file");
            }

            Log.Warn($"RouteSuggest: Successfully loaded {PathConfigs.Count} path configs from {configPath}");
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Failed to load config file: {ex.Message}, using defaults");
        }
    }

    /// <summary>
    /// Pretty prints all current path configurations for debugging.
    /// </summary>
    static void PrintPathConfigs()
    {
        Log.Warn("RouteSuggest: Current Path Configurations:");
        Log.Warn("==========================================");

        foreach (var config in PathConfigs)
        {
            Log.Warn($"  Path: {config.Name}");
            Log.Warn($"    Priority: {config.Priority}");
            Log.Warn($"    Color: R={config.Color.R:F2}, G={config.Color.G:F2}, B={config.Color.B:F2}, A={config.Color.A:F2}");
            Log.Warn($"    Scoring Weights:");

            foreach (var weight in config.ScoringWeights.OrderBy(w => w.Key.ToString()))
            {
                Log.Warn($"      {weight.Key}: {weight.Value:+0;-0;0}");
            }

            Log.Warn("");
        }

        Log.Warn($"Total path types configured: {PathConfigs.Count}");
        Log.Warn("==========================================");
    }

    /// <summary>
    /// Parses a color string in hex format (#RGB or #RGBA) into a Godot Color.
    /// </summary>
    /// <param name="colorStr">Hex color string, e.g., "#FFD700" or "#FF0000FF"</param>
    /// <returns>Parsed Color, or white if parsing fails.</returns>
    static Color ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return new Color(1f, 1f, 1f, 1f);

        // Support hex format like "#FFD700" or "#FFD700FF"
        if (colorStr.StartsWith("#"))
        {
            colorStr = colorStr.Substring(1);
            if (colorStr.Length == 6)
            {
                // RGB format
                float r = Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f;
                return new Color(r, g, b, 1f);
            }
            else if (colorStr.Length == 8)
            {
                // RGBA format
                float r = Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f;
                float a = Convert.ToInt32(colorStr.Substring(6, 2), 16) / 255f;
                return new Color(r, g, b, a);
            }
        }

        // Fallback: return white
        return new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Parses scoring weights from the config file's string-keyed dictionary to MapPointType keys.
    /// </summary>
    /// <param name="weightsDict">Dictionary with string keys from JSON.</param>
    /// <returns>Dictionary with MapPointType keys, containing valid entries only.</returns>
    static Dictionary<MapPointType, int> ParseScoringWeights(Dictionary<string, int> weightsDict)
    {
        var result = new Dictionary<MapPointType, int>();
        if (weightsDict == null) return result;

        foreach (var kvp in weightsDict)
        {
            if (Enum.TryParse<MapPointType>(kvp.Key, out var pointType))
            {
                result[pointType] = kvp.Value;
            }
            else
            {
                Log.Warn($"RouteSuggest: Unknown MapPointType '{kvp.Key}' in config");
            }
        }
        return result;
    }

    /// <summary>
    /// Represents the root structure of the RouteSuggestConfig.json file.
    /// </summary>
    private class ConfigFile
    {
        /// <summary>
        /// Schema version for config file compatibility. Currently must be 1.
        /// </summary>
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        /// <summary>
        /// List of path configurations to load.
        /// </summary>
        [JsonPropertyName("path_configs")]
        public List<PathConfigEntry> PathConfigs { get; set; }
    }

    /// <summary>
    /// Represents a single path configuration entry in the JSON config file.
    /// </summary>
    private class PathConfigEntry
    {
        /// <summary>
        /// Name identifier for the path.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Hex color string (e.g., "#FFD700").
        /// </summary>
        [JsonPropertyName("color")]
        public string Color { get; set; }

        /// <summary>
        /// Priority value - higher renders on top.
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        /// <summary>
        /// Scoring weights as string-keyed dictionary (keys are MapPointType names).
        /// </summary>
        [JsonPropertyName("scoring_weights")]
        public Dictionary<string, int> ScoringWeights { get; set; }
    }

    /// <summary>
    /// Initializes reflection to access the map screen's internal _paths field.
    /// Required for highlighting path segments on the UI.
    /// </summary>
    static void InitializeReflection()
    {
        try
        {
            var mapScreenType = typeof(NMapScreen);
            _pathsField = mapScreenType.GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
            _reflectionInitialized = _pathsField != null;
            if (_reflectionInitialized)
            {
                Log.Warn("RouteSuggest: Reflection initialized successfully");
            }
            else
            {
                Log.Warn("RouteSuggest: Failed to initialize reflection - _paths field not found");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Error initializing reflection: {ex.Message}");
            _reflectionInitialized = false;
        }
    }

    /// <summary>
    /// Called when a new run starts. Stores the run state and calculates initial paths.
    /// </summary>
    static void OnRunStarted(RunState runState)
    {
        Log.Warn("RouteSuggest: Run started");
        RouteSuggest.RunState = runState;
        UpdateBestPath();
    }

    /// <summary>
    /// Called when entering a new act. Recalculates paths and requests map highlighting.
    /// </summary>
    static void OnActEntered()
    {
        Log.Warn("RouteSuggest: Act entered");
        UpdateBestPath();
        RequestHighlightOnMapOpen();
    }

    /// <summary>
    /// Called when entering a room. Recalculates paths and requests map highlighting.
    /// </summary>
    static void OnRoomEntered()
    {
        Log.Warn("RouteSuggest: Room entered");
        UpdateBestPath();
        RequestHighlightOnMapOpen();
    }

    /// <summary>
    /// Requests path highlighting. If map screen is already open, highlights immediately.
    /// Otherwise, subscribes to the Opened event to highlight when it opens.
    /// </summary>
    static void RequestHighlightOnMapOpen()
    {
        Log.Warn("RouteSuggest: Requesting highlight when map opens");

        // Check if map screen is already open
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null && mapScreen.IsOpen)
        {
            Log.Warn("RouteSuggest: Map screen is already open, highlighting immediately");
            HighlightBestPath();
            return;
        }

        _pendingHighlight = true;

        // Subscribe to map screen opened event
        if (mapScreen != null)
        {
            mapScreen.Opened += OnMapScreenOpened;
            Log.Warn("RouteSuggest: Subscribed to map screen Opened event");
        }
        else
        {
            Log.Warn("RouteSuggest: NMapScreen.Instance is null, will retry on next act/room");
        }
    }

    /// <summary>
    /// Event handler for when the map screen opens. Applies pending highlights.
    /// </summary>
    static void OnMapScreenOpened()
    {
        Log.Warn("RouteSuggest: Map screen opened event triggered");

        if (!_pendingHighlight)
        {
            Log.Warn("RouteSuggest: No pending highlight, ignoring");
            return;
        }

        _pendingHighlight = false;

        // Unsubscribe from the event
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null)
        {
            mapScreen.Opened -= OnMapScreenOpened;
            Log.Warn("RouteSuggest: Unsubscribed from map screen Opened event");
        }

        // Apply the highlight
        HighlightBestPath();
    }

    /// <summary>
    /// Called when exiting a room. Recalculates paths for the new position.
    /// </summary>
    static void OnRoomExited()
    {
        Log.Warn("RouteSuggest: Room exited");
        UpdateBestPath();
    }

    /// <summary>
    /// Calculates optimal paths for all configured path types from current position.
    /// Uses CurrentMapPoint if available, otherwise falls back to StartingMapPoint.
    /// </summary>
    static void UpdateBestPath()
    {
        var runState = RouteSuggest.RunState;
        Log.Warn($"RouteSuggest: Current act index {runState.CurrentActIndex}");
        Log.Warn($"RouteSuggest: Floor {runState.ActFloor}/{runState.TotalFloor}");

        // Get current position, fallback to starting point if not set
        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;

        if (startPoint != null)
        {
            Log.Warn($"RouteSuggest: At map point {startPoint.coord}");

            // Calculate paths for all configured path types
            CalculatedPaths.Clear();
            foreach (var config in PathConfigs)
            {
                var path = FindBestPath(startPoint, config);
                if (path != null)
                {
                    int score = config.CalculateScore(path);
                    Log.Warn($"RouteSuggest: {config.Name} path found with score {score}:");
                    foreach (var point in path)
                    {
                        Log.Warn($"RouteSuggest:   coord={point.coord}, type={point.PointType}");
                    }
                    CalculatedPaths[config.Name] = path;
                }
            }
        }
    }

    /// <summary>
    /// Finds the best path from startPoint to the Boss using the given configuration's scoring.
    /// Uses DFS to enumerate all paths, sorts for reproducibility, and returns the highest scoring path.
    /// </summary>
    /// <param name="startPoint">Starting map point.</param>
    /// <param name="config">Path configuration with scoring weights.</param>
    /// <returns>The best path, or null if no paths found or startPoint is null.</returns>
    static List<MapPoint> FindBestPath(MapPoint startPoint, PathConfig config)
    {
        if (startPoint == null) return null;

        // DFS to find all paths to Boss
        var currentPath = new List<MapPoint>();
        var allPaths = new List<List<MapPoint>>();
        FindAllPathsToBoss(startPoint, currentPath, allPaths);

        // Sort paths by their coordinate sequence for reproducibility
        allPaths.Sort((a, b) =>
        {
            int minLen = Math.Min(a.Count, b.Count);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = a[i].coord.CompareTo(b[i].coord);
                if (cmp != 0) return cmp;
            }
            return a.Count.CompareTo(b.Count);
        });

        Log.Warn($"RouteSuggest: Found {allPaths.Count} path(s) to Boss");
        for (int i = 0; i < allPaths.Count; i++)
        {
            Log.Warn($"RouteSuggest: Path {i + 1}:");
            foreach (var point in allPaths[i])
            {
                Log.Warn($"RouteSuggest:   coord={point.coord}, type={point.PointType}");
            }
        }

        // Calculate scores for each path and find the best
        if (allPaths.Count == 0)
        {
            return null;
        }

        int bestScore = int.MinValue;
        List<MapPoint> bestPath = null;

        for (int i = 0; i < allPaths.Count; i++)
        {
            int score = config.CalculateScore(allPaths[i]);
            Log.Warn($"RouteSuggest: Path {i + 1} score: {score}");

            if (score > bestScore)
            {
                bestScore = score;
                bestPath = allPaths[i];
            }
        }

        return bestPath;
    }

    /// <summary>
    /// Recursive DFS helper to find all paths from current point to the Boss.
    /// </summary>
    /// <param name="current">Current map point.</param>
    /// <param name="currentPath">Accumulated path so far.</param>
    /// <param name="allPaths">Output list to collect all complete paths.</param>
    static void FindAllPathsToBoss(MapPoint current, List<MapPoint> currentPath, List<List<MapPoint>> allPaths)
    {
        if (current == null) return;

        // Add current point to path
        currentPath.Add(current);

        // Check if we reached the Boss
        if (current.PointType == MapPointType.Boss)
        {
            // Found a path to Boss, save a copy
            allPaths.Add(new List<MapPoint>(currentPath));
        }
        else if (current.Children != null && current.Children.Count > 0)
        {
            // Continue DFS on children
            foreach (var child in current.Children)
            {
                FindAllPathsToBoss(child, currentPath, allPaths);
            }
        }

        // Backtrack: remove current point from path
        currentPath.RemoveAt(currentPath.Count - 1);
    }

    /// <summary>
    /// Highlights the calculated paths on the map screen. Higher priority paths render on top.
    /// Uses reflection to access the map's internal path rendering data.
    /// </summary>
    static void HighlightBestPath()
    {
        if (!_reflectionInitialized)
        {
            Log.Warn("RouteSuggest: Highlighting skipped - reflection not initialized");
            return;
        }
        if (CalculatedPaths.Count == 0)
        {
            Log.Warn("RouteSuggest: Highlighting skipped - no paths calculated");
            return;
        }

        try
        {
            // Clear any previous highlighting first
            ClearPathHighlighting();

            // Get the map screen instance
            var mapScreen = NMapScreen.Instance;
            if (mapScreen == null)
            {
                Log.Warn("RouteSuggest: Highlighting skipped - NMapScreen.Instance is null");
                return;
            }
            if (!mapScreen.IsOpen)
            {
                Log.Warn("RouteSuggest: Highlighting skipped - Map screen is not open");
                return;
            }

            // Get the _paths dictionary using reflection
            var paths = _pathsField.GetValue(mapScreen) as System.Collections.IDictionary;
            if (paths == null)
            {
                Log.Warn("RouteSuggest: Failed to get paths dictionary");
                return;
            }

            // Build segment sets for each path type
            var pathSegments = new Dictionary<string, HashSet<(MapCoord, MapCoord)>>();
            foreach (var kvp in CalculatedPaths)
            {
                var path = kvp.Value;
                if (path != null && path.Count >= 2)
                {
                    var segments = new HashSet<(MapCoord, MapCoord)>();
                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        segments.Add((path[i].coord, path[i + 1].coord));
                    }
                    pathSegments[kvp.Key] = segments;
                }
            }

            // Assign color to each segment based on priority (highest priority wins)
            var segmentColors = new Dictionary<(MapCoord, MapCoord), Color>();
            var sortedConfigs = PathConfigs.OrderByDescending(c => c.Priority).ToList();

            foreach (var config in sortedConfigs)
            {
                if (!pathSegments.TryGetValue(config.Name, out var segments))
                    continue;

                foreach (var segment in segments)
                {
                    // Normalize segment direction for deduplication
                    var normalizedKey = segment.Item1.CompareTo(segment.Item2) <= 0
                        ? segment
                        : (segment.Item2, segment.Item1);

                    // Assign color if not already assigned (higher priority paths win)
                    if (!segmentColors.ContainsKey(normalizedKey))
                    {
                        segmentColors[normalizedKey] = config.Color;
                    }
                }
            }

            // Apply highlighting to all unique segments
            foreach (var kvp in segmentColors)
            {
                var segment = kvp.Key;
                var color = kvp.Value;

                // Try both directions (from->to and to->from)
                var key1 = segment;
                var key2 = (segment.Item2, segment.Item1);

                object pathTicks = null;
                if (paths.Contains(key1))
                {
                    pathTicks = paths[key1];
                }
                else if (paths.Contains(key2))
                {
                    pathTicks = paths[key2];
                }

                if (pathTicks != null)
                {
                    var ticks = pathTicks as IReadOnlyList<TextureRect>;
                    if (ticks != null)
                    {
                        foreach (var tick in ticks)
                        {
                            if (tick != null && GodotObject.IsInstanceValid(tick))
                            {
                                // Save original color and scale if not already saved
                                if (!OriginalTickProperties.ContainsKey(tick))
                                {
                                    OriginalTickProperties[tick] = (tick.Modulate, tick.Scale);
                                }
                                tick.Modulate = color;
                                tick.Scale = new Vector2(1.4f, 1.4f);
                            }
                        }
                    }
                }
            }

            Log.Warn($"RouteSuggest: Highlighted {segmentColors.Count} unique path segments");
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Error highlighting path: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all path highlighting by restoring original colors and scales.
    /// Also cleans up invalid TextureRect entries from the cache.
    /// </summary>
    static void ClearPathHighlighting()
    {
        try
        {
            // Reset highlighted path ticks to their original color and scale
            var ticksToRemove = new List<TextureRect>();
            foreach (var kvp in OriginalTickProperties)
            {
                var tick = kvp.Key;
                var (originalColor, originalScale) = kvp.Value;

                if (tick != null && GodotObject.IsInstanceValid(tick))
                {
                    tick.Modulate = originalColor;
                    tick.Scale = originalScale;
                }
                else
                {
                    // Mark invalid ticks for removal
                    ticksToRemove.Add(tick);
                }
            }

            // Clean up invalid entries
            foreach (var tick in ticksToRemove)
            {
                OriginalTickProperties.Remove(tick);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"RouteSuggest: Error clearing path highlighting: {ex.Message}");
        }
    }
}
