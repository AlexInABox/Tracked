using System.Collections.Generic;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using UnityEngine;

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
        ServerEvents.RoundEnded += OnRoundEnded;
    }

    public static void UnregisterEvents()
    {
        PlayerEvents.Joined -= OnJoined;
        PlayerEvents.Left -= OnLeft;
        ServerEvents.RoundEnded -= OnRoundEnded;
    }

    private static void OnJoined(PlayerJoinedEventArgs ev)
    {
        string userId = ev.Player.UserId;
        int timestamp = (int)Time.time;

        PlayerStartingTimestamps[userId] = timestamp;

        if (!PlayerTimePlayedThisRound.ContainsKey(userId)) PlayerTimePlayedThisRound[userId] = 0;
    }

    private static void OnLeft(PlayerLeftEventArgs ev)
    {
        string userId = ev.Player.UserId;
        int timestamp = (int)Time.time;

        PlayerTimePlayedThisRound[userId] += timestamp - PlayerStartingTimestamps[userId];
    }

    private static void OnRoundEnded(RoundEndedEventArgs ev)
    {
        int endTimestamp = (int)Time.time;

        foreach (Player player in Player.List)
        {
            string userId = player.UserId;

            PlayerTimePlayedThisRound[userId] += endTimestamp - PlayerStartingTimestamps[userId];
        }


        UploadTimesToDatabase();
    }

    private static void UploadTimesToDatabase()
    {
        // This method should be implemented to upload the PlayerTimePlayedThisRound data to the database.
        // It is currently a placeholder and needs to be filled with actual database interaction logic.
    }
}