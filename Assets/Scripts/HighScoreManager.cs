using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Data structure representing the high score record for an individual map.
/// Serialized to and from JSON using Unity's JsonUtility.
/// </summary>
[System.Serializable]
public class MapScoreRecord
{
    [Tooltip("The human-readable display name of the map (e.g. 'Checkmate').")]
    public string mapName;

    [Tooltip("The zero-based index of the map in MapSelectManager.")]
    public int mapIndex;

    [Tooltip("The highest score achieved on this map.")]
    public int highScore;

    [Tooltip("The time in seconds elapsed during the run that set this high score.")]
    public float timePlayedSeconds;

    [Tooltip("The formatted duration of the run (HH:MM:SS or MM:SS).")]
    public string formattedTime;

    [Tooltip("The UTC timestamp when this high score was set.")]
    public string dateAchieved;

    public MapScoreRecord() { }

    public MapScoreRecord(string name, int index, int score, float timeSeconds)
    {
        mapName = name ?? string.Empty;
        mapIndex = index;
        highScore = score;
        timePlayedSeconds = timeSeconds;
        formattedTime = HighScoreManager.FormatDuration(timeSeconds);
        dateAchieved = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

/// <summary>
/// Root container object holding the list of all map high score records for JsonUtility.
/// </summary>
[System.Serializable]
public class HighScoreCollection
{
    public List<MapScoreRecord> records = new List<MapScoreRecord>();
}

/// <summary>
/// Manages loading, saving, and querying high score records per map stored in a persistent JSON file.
/// </summary>
public static class HighScoreManager
{
    private const string FileName = "highscores.json";

    /// <summary>
    /// Absolute path to the high scores JSON save file in Application.persistentDataPath.
    /// </summary>
    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>
    /// Event invoked whenever a high score is added or updated.
    /// </summary>
    public static event Action<MapScoreRecord> OnHighScoreChanged;

    private static HighScoreCollection cachedCollection;
    private static bool isLoaded = false;

    /// <summary>
    /// Loads high score records from disk. If the file does not exist, an empty collection is created.
    /// </summary>
    public static HighScoreCollection LoadHighScores(bool forceReload = false)
    {
        if (isLoaded && !forceReload && cachedCollection != null)
        {
            return cachedCollection;
        }

        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    cachedCollection = JsonUtility.FromJson<HighScoreCollection>(json);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HighScoreManager] Failed to load high scores from '{FilePath}': {ex.Message}");
        }

        if (cachedCollection == null)
        {
            cachedCollection = new HighScoreCollection();
        }

        if (cachedCollection.records == null)
        {
            cachedCollection.records = new List<MapScoreRecord>();
        }

        isLoaded = true;
        return cachedCollection;
    }

    /// <summary>
    /// Serializes the current high score collection to the persistent JSON file on disk.
    /// </summary>
    public static bool SaveHighScores()
    {
        if (cachedCollection == null)
        {
            cachedCollection = new HighScoreCollection();
        }

        try
        {
            string path = FilePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(cachedCollection, true);
            File.WriteAllText(path, json);
            Debug.Log($"[HighScoreManager] High scores successfully saved to: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HighScoreManager] Failed to write high scores to '{FilePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Retrieves the high score record for the specified map by index or map name.
    /// </summary>
    public static MapScoreRecord GetRecord(int mapIndex, string mapName = null)
    {
        LoadHighScores();

        if (cachedCollection == null || cachedCollection.records == null)
        {
            return null;
        }

        // 1. Try matching both index and name if name is provided
        if (!string.IsNullOrEmpty(mapName) && mapIndex >= 0)
        {
            for (int i = 0; i < cachedCollection.records.Count; i++)
            {
                var r = cachedCollection.records[i];
                if (r.mapIndex == mapIndex && string.Equals(r.mapName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    return r;
                }
            }
        }

        // 2. Try matching by index
        if (mapIndex >= 0)
        {
            for (int i = 0; i < cachedCollection.records.Count; i++)
            {
                var r = cachedCollection.records[i];
                if (r.mapIndex == mapIndex)
                {
                    return r;
                }
            }
        }

        // 3. Try matching by normalized map name
        if (!string.IsNullOrEmpty(mapName))
        {
            for (int i = 0; i < cachedCollection.records.Count; i++)
            {
                var r = cachedCollection.records[i];
                if (string.Equals(r.mapName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    return r;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a valid high score (> 0) has been recorded for the specified map.
    /// </summary>
    public static bool HasRecord(int mapIndex, string mapName = null)
    {
        MapScoreRecord record = GetRecord(mapIndex, mapName);
        return record != null && record.highScore > 0;
    }

    /// <summary>
    /// Evaluates a finished game run and records it if it achieves a new high score for the map.
    /// Returns true if a record was updated or created.
    /// </summary>
    public static bool TryRecordScore(string mapName, int mapIndex, int score, float timeSeconds, out bool isNewRecord)
    {
        LoadHighScores();
        isNewRecord = false;

        if (score <= 0)
        {
            return false;
        }

        MapScoreRecord existing = GetRecord(mapIndex, mapName);

        if (existing != null)
        {
            // New record if score is strictly higher, or if score is tied with a faster time
            if (score > existing.highScore || (score == existing.highScore && timeSeconds < existing.timePlayedSeconds))
            {
                existing.mapName = string.IsNullOrEmpty(mapName) ? existing.mapName : mapName;
                existing.mapIndex = mapIndex >= 0 ? mapIndex : existing.mapIndex;
                existing.highScore = score;
                existing.timePlayedSeconds = timeSeconds;
                existing.formattedTime = FormatDuration(timeSeconds);
                existing.dateAchieved = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                SaveHighScores();
                isNewRecord = true;
                OnHighScoreChanged?.Invoke(existing);
                return true;
            }

            return false;
        }

        // No record exists yet: create a new record
        MapScoreRecord newRecord = new MapScoreRecord(mapName, mapIndex, score, timeSeconds);
        cachedCollection.records.Add(newRecord);
        SaveHighScores();

        isNewRecord = true;
        OnHighScoreChanged?.Invoke(newRecord);
        return true;
    }

    /// <summary>
    /// Formats total seconds into HH:MM:SS or MM:SS format.
    /// </summary>
    public static string FormatDuration(float totalSeconds)
    {
        if (totalSeconds < 0f) totalSeconds = 0f;
        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);

        if (time.TotalHours >= 1.0)
        {
            return string.Format("{0:00}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds);
        }

        return string.Format("{0:00}:{1:00}", time.Minutes, time.Seconds);
    }

    /// <summary>
    /// Clears all stored high scores in memory and on disk.
    /// </summary>
    public static void ClearAllHighScores()
    {
        cachedCollection = new HighScoreCollection();
        SaveHighScores();
    }
}
