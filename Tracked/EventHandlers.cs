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
    private static readonly Dictionary<string, int> PlayerStartingTimestamps = new();
    private static readonly Dictionary<string, int> PlayerTimePlayedThisRound = new();


    public static void RegisterEvents()
    {
        // Starting conditions
        PlayerEvents.Joined += OnJoined;

        // Ending calculate conditions
        PlayerEvents.Left += OnLeft;
        ServerEvents.RoundEnding += OnRoundEnding;
    }

    public static void UnregisterEvents()
    {
        PlayerEvents.Joined -= OnJoined;
        PlayerEvents.Left -= OnLeft;
        ServerEvents.RoundEnding -= OnRoundEnding;
    }

    private static void OnJoined(PlayerJoinedEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost) return;

        var userId = ev.Player.UserId;
        var timestamp = (int)Time.time;

        PlayerStartingTimestamps[userId] = timestamp;

        if (!PlayerTimePlayedThisRound.ContainsKey(userId)) PlayerTimePlayedThisRound[userId] = 0;
    }

    private static void OnLeft(PlayerLeftEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost) return;

        var userId = ev.Player.UserId;
        var timestamp = (int)Time.time;

        if (!PlayerStartingTimestamps.ContainsKey(userId)) return;
        
        PlayerTimePlayedThisRound[userId] += timestamp - PlayerStartingTimestamps[userId];
        PlayerStartingTimestamps.Remove(userId);
    }

    private static void OnRoundEnding(RoundEndingEventArgs ev)
    {
        var endTimestamp = (int)Time.time;

        foreach (var player in Player.List)
        {
            if (player.IsDummy || player.IsHost) continue;

            var userId = player.UserId;
            
            if (string.IsNullOrEmpty(userId)) continue; //HATE. LET ME TELL YOU HOW MUCH I'VE COME TO HATE LABAPI!!

            if (!PlayerStartingTimestamps.ContainsKey(userId)) continue;

            PlayerTimePlayedThisRound[userId] += endTimestamp - PlayerStartingTimestamps[userId];
        }


        UploadTimesToDatabase();
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
                var response = await client.PostAsync(config.EndpointUrl, content);

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
}