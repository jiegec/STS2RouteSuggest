using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public string Name { get; set; }
    public Color Color { get; set; }
    /// <summary>
    /// Higher priority paths are rendered on top of lower priority paths when they overlap.
    /// </summary>
    public int Priority { get; set; }
    /// <summary>
    /// Scoring weights for each room type. Positive = desirable, Negative = undesirable.
    /// </summary>
    public Dictionary<MapPointType, int> ScoringWeights { get; set; } = new Dictionary<MapPointType, int>();

    public int CalculateScore(List<MapPoint> path)
    {
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

[ModInitializer("ModLoaded")]
public static class RouteSuggest
{
    public static RunState RunState { get; private set; }

    /// <summary>
    /// Dictionary of calculated paths for each configured path type.
    /// Key is the path config name, value is the calculated path.
    /// </summary>
    public static Dictionary<string, List<MapPoint>> CalculatedPaths { get; private set; } = new Dictionary<string, List<MapPoint>>();

    /// <summary>
    /// List of configured path types. Add new entries here to support more path types.
    /// </summary>
    public static readonly List<PathConfig> PathConfigs = new List<PathConfig>
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

    // Store original colors for each path tick to restore later
    private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTickProperties = new Dictionary<TextureRect, (Color, Vector2)>();

    // Cache for reflection
    private static FieldInfo _pathsField;
    private static bool _reflectionInitialized = false;

    // Track if we need to highlight when map opens
    private static bool _pendingHighlight = false;

    public static void ModLoaded()
    {
        Log.Warn("RouteSuggest: Mod loaded");

        // listen to events
        var manager = RunManager.Instance;
        manager.RunStarted += OnRunStarted;
        manager.ActEntered += OnActEntered;
        manager.RoomEntered += OnRoomEntered;
        manager.RoomExited += OnRoomExited;

        // Initialize reflection for path highlighting
        InitializeReflection();
    }

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

    static void OnRunStarted(RunState runState)
    {
        Log.Warn("RouteSuggest: Run started");
        RouteSuggest.RunState = runState;
        UpdateBestPath();
    }

    static void OnActEntered()
    {
        Log.Warn("RouteSuggest: Act entered");
        UpdateBestPath();

        // Request highlight when map screen opens
        RequestHighlightOnMapOpen();
    }

    static void OnRoomEntered()
    {
        Log.Warn("RouteSuggest: Room entered");
        UpdateBestPath();

        // Request highlight when map screen opens
        RequestHighlightOnMapOpen();
    }

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

    static void OnRoomExited()
    {
        Log.Warn("RouteSuggest: Room exited");
        UpdateBestPath();
    }

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
