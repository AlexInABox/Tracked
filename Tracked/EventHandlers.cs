using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Newtonsoft.Json;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Tracked;

public static class EventHandlers
{
    //Helper dictionaries
    private static readonly Dictionary<string, int> PlayerStartingTimestamps = new();
    private static int RoundStartTimestamp;

    //Publish dictionaries
    private static readonly Dictionary<string, int> PlayerTimePlayedThisRound = new();
    private static readonly Dictionary<string, int> PlayerRoundsPlayedThisRound = new();
    private static readonly List<KillRecord> KillsThisRound = [];
    private static readonly Dictionary<string, int> PlayerMedkitsUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerColasUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerAdrenalineUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerPocketEscapesThisRound = new();

    public static void RegisterEvents()
    {
        // Starting conditions
        PlayerEvents.Joined += OnJoined;
        ServerEvents.RoundStarting += OnRoundStarting;

        // Kill stuff
        PlayerEvents.Death += OnDeath;

        //Events stuff
        PlayerEvents.UsedItem += OnUsedItem;
        PlayerEvents.LeftPocketDimension += OnLeftPocketDimension;

        // Ending calculate conditions
        PlayerEvents.Left += OnLeft;
        ServerEvents.RoundEnding += OnRoundEnding;
    }

    public static void UnregisterEvents()
    {
        PlayerEvents.Joined -= OnJoined;
        PlayerEvents.Left -= OnLeft;
        PlayerEvents.Death -= OnDeath;
        PlayerEvents.UsedItem -= OnUsedItem;
        PlayerEvents.LeftPocketDimension -= OnLeftPocketDimension;
        ServerEvents.RoundStarting -= OnRoundStarting;
        ServerEvents.RoundEnding -= OnRoundEnding;
    }

    private static void OnJoined(PlayerJoinedEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        var userId = ev.Player.UserId;
        var timestamp = (int)Time.time;

        PlayerStartingTimestamps[userId] = timestamp;

        if (!PlayerTimePlayedThisRound.ContainsKey(userId)) PlayerTimePlayedThisRound[userId] = 0;
        if (!PlayerRoundsPlayedThisRound.ContainsKey(userId)) PlayerRoundsPlayedThisRound[userId] = 0;
    }

    private static void OnLeft(PlayerLeftEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        var userId = ev.Player.UserId;
        var timestamp = (int)Time.time;

        if (!PlayerStartingTimestamps.ContainsKey(userId)) return;

        PlayerTimePlayedThisRound[userId] += timestamp - PlayerStartingTimestamps[userId];
        PlayerStartingTimestamps.Remove(userId);
    }

    private static void OnDeath(PlayerDeathEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost) return;

        // Check if ServerLogsText contains "unknown" (any spelling) - indicates disconnect, should not be counted
        var serverLogsText = ev.DamageHandler.ServerLogsText;
        if (string.IsNullOrEmpty(serverLogsText) ||
            serverLogsText.ToLower().Contains("unknown"))
        {
            Logger.Debug($"Skipping kill record due to disconnect: {serverLogsText}");
            return;
        }

        var timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(); //epoch
        var targetId = "anonymous";
        var attackerId = "anonymous";

        if (!ev.Player.DoNotTrack) targetId = ev.Player.UserId;
        if (ev.Attacker != null && !ev.Attacker.DoNotTrack && !ev.Attacker.IsHost && !ev.Attacker.IsDummy)
            attackerId = ev.Attacker.UserId;

        KillsThisRound.Add(new KillRecord(
            attackerId,
            targetId,
            timestamp
        ));

        Logger.Debug($"Kill recorded: {attackerId} -> {targetId} at {timestamp}");
    }

    private static void OnUsedItem(PlayerUsedItemEventArgs ev)
    {
        var isMedkit = ev.UsableItem.Type == ItemType.Medkit;
        var isAdrenaline = ev.UsableItem.Type == ItemType.Adrenaline;
        var isCola = ev.UsableItem.Type == ItemType.SCP207;

        if (!isMedkit && !isAdrenaline && !isCola) return;

        var userId = ev.Player.UserId;
        if (string.IsNullOrEmpty(userId) || ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        if (isMedkit)
        {
            if (!PlayerMedkitsUsedThisRound.ContainsKey(userId))
                PlayerMedkitsUsedThisRound[userId] = 0;
            PlayerMedkitsUsedThisRound[userId]++;
            Logger.Debug($"Player {userId} used a medkit. Total this round: {PlayerMedkitsUsedThisRound[userId]}");
        }
        else if (isCola)
        {
            if (!PlayerColasUsedThisRound.ContainsKey(userId))
                PlayerColasUsedThisRound[userId] = 0;
            PlayerColasUsedThisRound[userId]++;
            Logger.Debug($"Player {userId} used a cola. Total this round: {PlayerColasUsedThisRound[userId]}");
        }
        else if (isAdrenaline)
        {
            if (!PlayerAdrenalineUsedThisRound.ContainsKey(userId))
                PlayerAdrenalineUsedThisRound[userId] = 0;
            PlayerAdrenalineUsedThisRound[userId]++;
            Logger.Debug($"Player {userId} used adrenaline. Total this round: {PlayerAdrenalineUsedThisRound[userId]}");
        }
    }

    private static void OnLeftPocketDimension(PlayerLeftPocketDimensionEventArgs ev)
    {
        if (!ev.IsSuccessful) return;

        var userId = ev.Player.UserId;
        if (string.IsNullOrEmpty(userId) || ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        if (!PlayerPocketEscapesThisRound.ContainsKey(userId))
            PlayerPocketEscapesThisRound[userId] = 0;
        PlayerPocketEscapesThisRound[userId]++;
        Logger.Debug(
            $"Player {userId} successfully escaped pocket dimension. Total this round: {PlayerPocketEscapesThisRound[userId]}");
    }

    private static void OnRoundStarting(RoundStartingEventArgs ev)
    {
        RoundStartTimestamp = (int)Time.time;
        PlayerRoundsPlayedThisRound.Clear();
        PlayerMedkitsUsedThisRound.Clear();
        PlayerColasUsedThisRound.Clear();
        PlayerAdrenalineUsedThisRound.Clear();
        PlayerPocketEscapesThisRound.Clear();
    }

    private static void OnRoundEnding(RoundEndingEventArgs ev)
    {
        var endTimestamp = (int)Time.time;
        var roundDuration = endTimestamp - RoundStartTimestamp;
        var minimumTimeForRound = roundDuration * 0.8; // 80% of round duration

        foreach (var player in Player.List)
        {
            if (player.IsDummy || player.IsHost || player.DoNotTrack) continue;

            var userId = player.UserId;

            if (string.IsNullOrEmpty(userId)) continue; //HATE. LET ME TELL YOU HOW MUCH I'VE COME TO HATE LABAPI!!

            if (!PlayerStartingTimestamps.ContainsKey(userId)) continue;

            PlayerTimePlayedThisRound[userId] += endTimestamp - PlayerStartingTimestamps[userId];

            // Check if player was present for at least 80% of the round
            if (PlayerTimePlayedThisRound[userId] >= minimumTimeForRound)
                PlayerRoundsPlayedThisRound[userId] = 1; // Player gets 1 round played
        }


        UploadTimesToDatabase();
        UploadKillsToDatabase();
        UploadRoundsToDatabase();
        UploadMedkitsToDatabase();
        UploadColasToDatabase();
        UploadAdrenalineToDatabase();
        UploadPocketEscapesToDatabase();
    }

    private static async void UploadTimesToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerTimePlayedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/times", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player times to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player times to database: {ex}");
        }

        PlayerStartingTimestamps.Clear();
        PlayerTimePlayedThisRound.Clear();
    }

    private static async void UploadKillsToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(KillsThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/kills", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player kills to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player kills to database: {ex}");
        }

        KillsThisRound.Clear();
    }

    private static async void UploadRoundsToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerRoundsPlayedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/rounds", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player rounds to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player rounds to database: {ex}");
        }

        PlayerRoundsPlayedThisRound.Clear();
    }

    private static async void UploadMedkitsToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerMedkitsUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/medkits", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player medkits usage to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player medkits usage to database: {ex}");
        }

        PlayerMedkitsUsedThisRound.Clear();
    }

    private static async void UploadColasToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerColasUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/colas", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player colas usage to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player colas usage to database: {ex}");
        }

        PlayerColasUsedThisRound.Clear();
    }

    private static async void UploadAdrenalineToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerAdrenalineUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/adrenaline", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player adrenaline usage to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player adrenaline usage to database: {ex}");
        }

        PlayerAdrenalineUsedThisRound.Clear();
    }

    private static async void UploadPocketEscapesToDatabase()
    {
        try
        {
            var config = Plugin.Instance.Config;
            var json = JsonConvert.SerializeObject(PlayerPocketEscapesThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(config.EndpointUrl + "/pocketescapes", content);

                var responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player pocket escapes to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player pocket escapes to database: {ex}");
        }

        PlayerPocketEscapesThisRound.Clear();
    }

    private class KillRecord(string attacker, string target, int timestamp)
    {
        public string Attacker { get; set; } = attacker;
        public string Target { get; set; } = target;
        public int Timestamp { get; set; } = timestamp;
    }
}