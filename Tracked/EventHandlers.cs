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

    public static void RegisterEvents()
    {
        // Starting conditions
        PlayerEvents.Joined += OnJoined;
        ServerEvents.RoundStarting += OnRoundStarting;

        // Kill stuff
        PlayerEvents.Death += OnDeath;

        // Ending calculate conditions
        PlayerEvents.Left += OnLeft;
        ServerEvents.RoundEnding += OnRoundEnding;
    }

    public static void UnregisterEvents()
    {
        PlayerEvents.Joined -= OnJoined;
        PlayerEvents.Left -= OnLeft;
        PlayerEvents.Death -= OnDeath;
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

    private static void OnRoundStarting(RoundStartingEventArgs ev)
    {
        RoundStartTimestamp = (int)Time.time;
        PlayerRoundsPlayedThisRound.Clear();
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

    private class KillRecord(string attacker, string target, int timestamp)
    {
        public string Attacker { get; set; } = attacker;
        public string Target { get; set; } = target;
        public int Timestamp { get; set; } = timestamp;
    }
}