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

[ModInitializer("ModLoaded")]
public static class RouteSuggest
{
    public static RunState RunState { get; private set; }
    public static List<MapPoint> BestPath { get; private set; }

    // Path highlighting color (gold/yellow)
    private static readonly Color SuggestedPathColor = new Color(1f, 0.84f, 0f, 1f); // Gold

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
        _pendingHighlight = true;

        // Subscribe to map screen opened event
        var mapScreen = NMapScreen.Instance;
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
        if (runState.CurrentMapPoint != null)
        {
            Log.Warn($"RouteSuggest: At map point {runState.CurrentMapPoint.coord}");

            // Find the best path using DFS
            var bestPath = FindBestPath(runState.CurrentMapPoint);

            if (bestPath != null)
            {
                int score = CalculatePathScore(bestPath);
                Log.Warn($"RouteSuggest: Best path found with score {score}:");
                foreach (var point in bestPath)
                {
                    Log.Warn($"RouteSuggest:   coord={point.coord}, type={point.PointType}");
                }

                // Store the best path for highlighting
                BestPath = bestPath;
            }
        }
    }

    static List<MapPoint> FindBestPath(MapPoint startPoint)
    {
        if (startPoint == null) return null;

        // DFS to find all paths to Boss
        var currentPath = new List<MapPoint>();
        var allPaths = new List<List<MapPoint>>();
        FindAllPathsToBoss(startPoint, currentPath, allPaths);

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
            int score = CalculatePathScore(allPaths[i]);
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

    static int CalculatePathScore(List<MapPoint> path)
    {
        int score = 0;
        foreach (var point in path)
        {
            switch (point.PointType)
            {
                case MapPointType.RestSite:
                case MapPointType.Treasure:
                case MapPointType.Shop:
                    score += 1;
                    break;
                case MapPointType.Monster:
                    score -= 1;
                    break;
                case MapPointType.Elite:
                    score -= 2;
                    break;
            }
        }
        return score;
    }

    static void HighlightBestPath()
    {
        if (!_reflectionInitialized)
        {
            Log.Warn("RouteSuggest: Highlighting skipped - reflection not initialized");
            return;
        }
        if (BestPath == null)
        {
            Log.Warn("RouteSuggest: Highlighting skipped - BestPath is null");
            return;
        }
        if (BestPath.Count < 2)
        {
            Log.Warn("RouteSuggest: Highlighting skipped - BestPath has less than 2 points");
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

            // Build a list of path segments (MapCoord pairs) from the best path
            var pathSegments = new List<(MapCoord from, MapCoord to)>();
            for (int i = 0; i < BestPath.Count - 1; i++)
            {
                pathSegments.Add((BestPath[i].coord, BestPath[i + 1].coord));
            }

            // Highlight each path segment
            int highlightedSegments = 0;
            foreach (var segment in pathSegments)
            {
                // Try both directions (from->to and to->from)
                var key1 = (segment.from, segment.to);
                var key2 = (segment.to, segment.from);

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
                    // pathTicks is IReadOnlyList<TextureRect>
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
                                tick.Modulate = SuggestedPathColor;
                                tick.Scale = new Vector2(1.4f, 1.4f); // Make slightly larger
                            }
                        }
                        highlightedSegments++;
                    }
                }
            }

            Log.Warn($"RouteSuggest: Highlighted {highlightedSegments} path segments");
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
