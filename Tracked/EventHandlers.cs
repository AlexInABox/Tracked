using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Exiled.API.Interfaces;
using Exiled.Loader;
using HintServiceMeow.Core.Enum;
using HintServiceMeow.Core.Models.Hints;
using HintServiceMeow.Core.Utilities;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using LabApi.Loader;
using MapGeneration;
using MEC;
using Newtonsoft.Json;
using PlayerRoles;
using SnakeAPI.API;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Tracked;

public static class EventHandlers
{
    //Globals for this file
    private static readonly Config Config = Plugin.Instance.Config!;

    //Helper dictionaries
    private static readonly Dictionary<string, int> PlayerStartingTimestamps = new();
    private static int _roundStartTimestamp;
    private static readonly Dictionary<string, int> ExtraPlayerPointsThisRound = new();
    private static readonly Dictionary<string, int> StoredPlayerPoints = new();

    //Publish dictionaries
    private static readonly Dictionary<string, int> PlayerTimePlayedThisRound = new();
    private static readonly Dictionary<string, int> PlayerRoundsPlayedThisRound = new();
    private static readonly List<KillRecord> KillsThisRound = [];
    private static readonly Dictionary<string, int> PlayerMedkitsUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerColasUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerAdrenalineUsedThisRound = new();
    private static readonly Dictionary<string, int> PlayerPocketEscapesThisRound = new();
    private static readonly Dictionary<string, int> PlayerPointsThisRound = new();
    private static readonly Dictionary<string, int> PlayerSnakeScoresThisRound = new();
    private static readonly Dictionary<string, bool> FakeRankAllowed = new();
    private static readonly Dictionary<string, bool> FakeRankAdmin = new();
    private static readonly Dictionary<string, string> PlayerUsernames = new();

    //RoundReports references
    private static IPlugin<IConfig> _roundReportsPlugin;
    private static Assembly _roundReportsAssembly;
    private static bool _foundRoundReports;
    private static Type _roundReportsApi;

    public static void RegisterEvents()
    {
        // Starting conditions
        PlayerEvents.Joined += OnJoined;
        ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
        ServerEvents.RoundStarting += OnRoundStarting;

        // Kill stuff
        PlayerEvents.Death += OnDeath;

        //Events stuff
        PlayerEvents.UsedItem += OnUsedItem;
        PlayerEvents.LeftPocketDimension += OnLeftPocketDimension;

        // Ending calculate conditions
        PlayerEvents.Left += OnLeft;
        ServerEvents.RoundEnding += OnRoundEnding;

        //Snake fun
        SnakeEvents.SnakeLost += OnSnakeGameFinished;

        //Map stuff
        //ServerEvents.MapGenerated += OnMapGenerated;
        ServerEvents.WaitingForPlayers += OnWaitingForPlayers;

        //Escaping logic
        PlayerEvents.Escaping += OnEscaping;

        //Points for window destructions
        PlayerEvents.DamagedWindow += OnDamagedWindow;
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
        SnakeEvents.SnakeLost -= OnSnakeGameFinished;
        //ServerEvents.MapGenerated -= OnMapGenerated;
        ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
        PlayerEvents.Escaping -= OnEscaping;
        PlayerEvents.DamagedWindow -= OnDamagedWindow;
    }

    private static void OnJoined(PlayerJoinedEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        string userId = ev.Player.UserId;
        int timestamp = (int)Time.time;

        PlayerStartingTimestamps[userId] = timestamp;
        PlayerUsernames[userId] = ev.Player.Nickname;

        if (!PlayerTimePlayedThisRound.ContainsKey(userId)) PlayerTimePlayedThisRound[userId] = 0;
        if (!PlayerRoundsPlayedThisRound.ContainsKey(userId)) PlayerRoundsPlayedThisRound[userId] = 0;

        //Check if the player is allowed to use fake rank
        FakeRankAllowed[ev.Player.UserId] = ev.Player.HasPermissions("fakerank");
        FakeRankAdmin[ev.Player.UserId] = ev.Player.HasPermissions("fakerank.admin");
        
        
        

        // Now initialize the HUD
        GetStoredZeitvertreibCoinsFromDatabase(userId);

        Hint hint = new()
        {
            Alignment = HintAlignment.Left,
            AutoText = _ =>
            {
                string hint = string.Empty;
                int zvc = 0;
                if (StoredPlayerPoints.TryGetValue(userId, out int storedPoints))
                    zvc += storedPoints;
                if (ExtraPlayerPointsThisRound.TryGetValue(userId, out int extraPoints))
                    zvc += extraPoints;
                zvc += GetPointsOfPlayer(ev.Player);
                hint += $"<size=20><b>Zeitvertreib Punkte: {zvc}</b></size>\n";
                return hint;
            },
            YCoordinateAlign = HintVerticalAlign.Bottom,
            YCoordinate = 995,
            XCoordinate = (int)(-540f * ev.Player.ReferenceHub.aspectRatioSync.AspectRatio + 600f) + 50,
            SyncSpeed = HintSyncSpeed.Slow
        };
        PlayerDisplay playerDisplay = PlayerDisplay.Get(ev.Player);
        playerDisplay.AddHint(hint);
    }

    private static void OnLeft(PlayerLeftEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        string userId = ev.Player.UserId;
        int timestamp = (int)Time.time;

        if (!PlayerStartingTimestamps.ContainsKey(userId)) return;

        PlayerTimePlayedThisRound[userId] += timestamp - PlayerStartingTimestamps[userId];
        PlayerStartingTimestamps.Remove(userId);

        PlayerPointsThisRound[userId] = GetPointsOfPlayer(ev.Player);
    }

    private static void OnDeath(PlayerDeathEventArgs ev)
    {
        if (ev.Player.IsDummy || ev.Player.IsHost) return;

        // Check if ServerLogsText contains "unknown" (any spelling) - indicates disconnect, should not be counted
        string serverLogsText = ev.DamageHandler.ServerLogsText;
        if (string.IsNullOrEmpty(serverLogsText) ||
            serverLogsText.ToLower().Contains("unknown"))
        {
            Logger.Debug($"Skipping kill record due to disconnect: {serverLogsText}");
            return;
        }

        int timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(); //epoch
        string targetId = "anonymous";
        string attackerId = "anonymous";

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
        bool isMedkit = ev.UsableItem.Type == ItemType.Medkit;
        bool isAdrenaline = ev.UsableItem.Type == ItemType.Adrenaline;
        bool isCola = ev.UsableItem.Type == ItemType.SCP207;

        if (!isMedkit && !isAdrenaline && !isCola) return;

        string userId = ev.Player.UserId;
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
        else
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

        string userId = ev.Player.UserId;
        if (string.IsNullOrEmpty(userId) || ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        if (!PlayerPocketEscapesThisRound.ContainsKey(userId))
            PlayerPocketEscapesThisRound[userId] = 0;
        PlayerPocketEscapesThisRound[userId]++;
        Logger.Debug(
            $"Player {userId} successfully escaped pocket dimension. Total this round: {PlayerPocketEscapesThisRound[userId]}");
    }

    private static void OnSnakeGameFinished(Player player, int score)
    {
        if (player.IsDummy || player.IsHost || player.DoNotTrack) return;

        string userId = player.UserId;
        if (string.IsNullOrEmpty(userId)) return; //HATE. LET ME TELL YOU HOW MUCH I'VE COME TO HATE LABAPI!!

        if (!PlayerSnakeScoresThisRound.ContainsKey(userId))
            PlayerSnakeScoresThisRound[userId] = 0;

        PlayerSnakeScoresThisRound[userId] = Math.Max(PlayerSnakeScoresThisRound[userId], score);
        Logger.Debug(
            $"Player {userId} finished snake game with score: {score}. Total this round: {PlayerSnakeScoresThisRound[userId]}");
    }

    private static void OnEscaping(PlayerEscapingEventArgs ev)
    {
        if (ev.OldRole is not (RoleTypeId.ClassD or RoleTypeId.Scientist)) return;

        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        int coinCount = ev.Player.Items.Count(item => item.Type == ItemType.Coin);
        if (coinCount == 0) return;

        if (ExtraPlayerPointsThisRound.ContainsKey(ev.Player.UserId))
            ExtraPlayerPointsThisRound[ev.Player.UserId] += coinCount * Plugin.Instance.Config!.CoinEscapeMultiplier;
        else
            ExtraPlayerPointsThisRound[ev.Player.UserId] = coinCount * Plugin.Instance.Config!.CoinEscapeMultiplier;

        Timing.CallDelayed(4f, () =>
        {
            string coinWord = coinCount == 1 ? "Münze" : "Münzen";
            string zvcCoinWord = coinCount * Plugin.Instance.Config!.CoinEscapeMultiplier == 1 ? "Münze" : "Münzen";


            ev.Player.SendHint(
                $"+ {coinCount * Plugin.Instance.Config!.CoinEscapeMultiplier} <b>Zeitvertreib {zvcCoinWord}</b>\n Grund: Entkommen mit {coinCount} {coinWord}",
                6f
            );
        });

        // Remove all coins from inventory
        //ev.Player.RemoveItem(ItemType.Coin, coinCount); <- doesn't work for some reason
        for (int i = 0; i < coinCount; i++) ev.Player.RemoveItem(ItemType.Coin);

        Logger.Debug(
            $"Player {ev.Player.UserId} escaped with {coinCount} coins, earning {coinCount * Plugin.Instance.Config!.CoinEscapeMultiplier} extra points.");
    }

    private static void OnDamagedWindow(PlayerDamagedWindowEventArgs ev)
    {
        if (!ev.Window.IsBroken) return;

        if (ev.Player.IsDummy || ev.Player.IsHost || ev.Player.DoNotTrack) return;

        if (ExtraPlayerPointsThisRound.ContainsKey(ev.Player.UserId))
            ExtraPlayerPointsThisRound[ev.Player.UserId] += 1;
        else
            ExtraPlayerPointsThisRound[ev.Player.UserId] = 1;

        ev.Player.SendHint(
            "+ 1 <b>Zeitvertreib Münze</b>\n Grund: Fenster zerstört",
            4f);

        Logger.Debug(
            $"Player {ev.Player.UserId} destroyed a window, earning 1 extra point.");
    }

    private static void OnWaitingForPlayers()
    {
        PlayerRoundsPlayedThisRound.Clear();
        PlayerMedkitsUsedThisRound.Clear();
        PlayerColasUsedThisRound.Clear();
        PlayerAdrenalineUsedThisRound.Clear();
        PlayerPocketEscapesThisRound.Clear();
        PlayerPointsThisRound.Clear();
        ExtraPlayerPointsThisRound.Clear();
        PlayerUsernames.Clear();

        List<TrackedRoom> map = [];
        foreach (Room room in Map.Rooms)
        {
            List<TrackedConnectedRoom> connectedRooms = [];
            foreach (RoomIdentifier connectedRoomIdentifier in room.ConnectedRooms)
                connectedRooms.Add(new TrackedConnectedRoom
                {
                    Pos = new TrackedCoordinates
                    {
                        X = (int)Math.Round(Room.Get(connectedRoomIdentifier).Position.x),
                        Z = (int)Math.Round(Room.Get(connectedRoomIdentifier).Position.z)
                    }
                });
            TrackedRoom newRoom = new()
            {
                Name = room.Name.ToString(),
                Shape = room.Shape.ToString(),
                Zone = room.Zone.ToString(),
                Pos = new TrackedCoordinates
                {
                    X = (int)Math.Round(room.Position.x),
                    Z = (int)Math.Round(room.Position.z)
                },
                ConnectedRooms = connectedRooms
            };
            map.Add(newRoom);
        }

        FileManager.WriteStringToFile(JsonConvert.SerializeObject(map, Formatting.Indented),
            Plugin.Instance.GetConfigPath(Plugin.Instance.ConfigFileName).Replace(Plugin.Instance.ConfigFileName, "") +
            "map.json");
    }

    private static void OnRoundStarting(RoundStartingEventArgs ev)
    {
        _roundStartTimestamp = (int)Time.time;

        ConnectToRoundReports();
    }

    private static void OnRoundEnding(RoundEndingEventArgs ev)
    {
        int endTimestamp = (int)Time.time;
        int roundDuration = endTimestamp - _roundStartTimestamp;
        double minimumTimeForRound = roundDuration * 0.8; // 80% of round duration

        foreach (Player player in Player.List)
        {
            if (player.IsDummy || player.IsHost || player.DoNotTrack) continue;

            string userId = player.UserId;

            if (string.IsNullOrEmpty(userId)) continue; //HATE. LET ME TELL YOU HOW MUCH I'VE COME TO HATE LABAPI!!

            if (!PlayerStartingTimestamps.ContainsKey(userId)) continue;

            PlayerTimePlayedThisRound[userId] += endTimestamp - PlayerStartingTimestamps[userId];

            // Check if player was present for at least 80% of the round
            if (PlayerTimePlayedThisRound[userId] >= minimumTimeForRound)
                PlayerRoundsPlayedThisRound[userId] = 1; // Player gets 1 round played
        }

        Dictionary<string, int> totalPlayerPointsTemp = new();
        foreach (Player player in Player.List)
        {
            if (player.IsDummy || player.IsHost || player.DoNotTrack) continue;
            string userId = player.UserId;

            if (string.IsNullOrEmpty(userId)) continue; //HATE. LET ME TELL YOU HOW MUCH I'VE COME TO HATE LABAPI!!
            PlayerPointsThisRound[userId] = GetPointsOfPlayer(player);

            totalPlayerPointsTemp[userId] = PlayerPointsThisRound[userId];

            if (ExtraPlayerPointsThisRound.TryGetValue(userId, out int extraPoints))
                totalPlayerPointsTemp[userId] += extraPoints;
        }

        UploadTimesToDatabase();
        UploadKillsToDatabase();
        UploadRoundsToDatabase();
        UploadMedkitsToDatabase();
        UploadColasToDatabase();
        UploadAdrenalineToDatabase();
        UploadPocketEscapesToDatabase();
        UploadPlayerPointsToDatabase(totalPlayerPointsTemp);
        UploadSnakeScoresToDatabase();
        UploadFakeRankAllowedToDatabase();
        UploadFakeRankAdminToDatabase();
        UploadUsernamesToDatabase();
    }

    private static async void UploadTimesToDatabase()
    {
        try
        {
            Config config = Plugin.Instance.Config!;
            string json = JsonConvert.SerializeObject(PlayerTimePlayedThisRound);

            Logger.Debug($"Uploading to endpoint: {config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(config.EndpointUrl + "/times", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(KillsThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Config.EndpointUrl + "/upload/kills", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(PlayerRoundsPlayedThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Config.EndpointUrl + "/upload/rounds", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(PlayerMedkitsUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Config.EndpointUrl + "/upload/medkits", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(PlayerColasUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Config.EndpointUrl + "/upload/colas", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(PlayerAdrenalineUsedThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/adrenaline", content);

                string responseText = await response.Content.ReadAsStringAsync();
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
            string json = JsonConvert.SerializeObject(PlayerPocketEscapesThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/pocketescapes", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded player pocket escapes to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload player pocket escapes to database: {ex}");
        }

        PlayerPocketEscapesThisRound.Clear();
    }

    private static async void UploadPlayerPointsToDatabase(Dictionary<string, int> totalPlayerPoints)
    {
        try
        {
            string json = JsonConvert.SerializeObject(totalPlayerPoints);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/playerpoints", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded XP to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload XP to database: {ex}");
        }

        PlayerPointsThisRound.Clear();
    }


    private static async void UploadSnakeScoresToDatabase()
    {
        try
        {
            string json = JsonConvert.SerializeObject(PlayerSnakeScoresThisRound);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Config.EndpointUrl + "/upload/snake", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded SnakeScore to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload SnakeScore to database: {ex}");
        }

        PlayerSnakeScoresThisRound.Clear();
    }

    private static async void UploadFakeRankAllowedToDatabase()
    {
        try
        {
            string json = JsonConvert.SerializeObject(FakeRankAllowed);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/fakerankallowed", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded FakeRankAllowed to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload FakeRankAllowed to database: {ex}");
        }

        FakeRankAllowed.Clear();
    }

    private static async void UploadFakeRankAdminToDatabase()
    {
        try
        {
            string json = JsonConvert.SerializeObject(FakeRankAdmin);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/fakerankadmin", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded FakeRankAdmin to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload FakeRankAdmin to database: {ex}");
        }

        FakeRankAllowed.Clear();
    }
    
    private static async void UploadUsernamesToDatabase()
    {
        try
        {
            string json = JsonConvert.SerializeObject(PlayerUsernames);

            Logger.Debug($"Uploading to endpoint: {Config.EndpointUrl}");
            Logger.Debug($"Payload: {json}");

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

                StringContent content = new(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response =
                    await client.PostAsync(Config.EndpointUrl + "/upload/usernames", content);

                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Info($"Uploaded usernames to database. Response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to upload usernames to database: {ex}");
        }

        FakeRankAllowed.Clear();
    }

    private static async void GetStoredZeitvertreibCoinsFromDatabase(string userId)
    {
        try
        {
            Logger.Debug($"{Config.EndpointUrl}/experience?userId={Uri.EscapeDataString(userId)}");
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

            HttpResponseMessage response = await client
                .GetAsync($"{Config.EndpointUrl}/experience?userId={Uri.EscapeDataString(userId)}")
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // Throw if not successful

            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Logger.Debug($"Fetched stored XP for user {userId} from database. Response: {responseText}");

            StoredPlayerPoints[userId] = int.TryParse(responseText, out int experience) ? experience : 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to fetch stored XP for user {userId}: {ex}");
        }
    }


    private static int GetPointsOfPlayer(Player player)
    {
        if (!_foundRoundReports || _roundReportsApi == null) return 0;
        if (player == null || player.IsDummy || player.IsHost || player.DoNotTrack) return 0;

        int points = 0;
        points = (int)_roundReportsApi.GetMethod("GetPointsOfPlayer")?.Invoke(null, [player.PlayerId])!;
        return points;
    }

    private static void ConnectToRoundReports()
    {
        _roundReportsPlugin = Loader.Plugins.FirstOrDefault(plugin => plugin.Assembly.GetName().Name == "RoundReports");
        _roundReportsAssembly = _roundReportsPlugin?.Assembly ?? null;
        _foundRoundReports = _roundReportsAssembly is not null;
        _roundReportsApi = _roundReportsAssembly?.GetType("RoundReports.API.RoundReports");

        if (_foundRoundReports && _roundReportsApi != null)
            Logger.Info("Connected to RoundReports API successfully.");
        else
            Logger.Warn("RoundReports API not found or failed to connect.");
    }

    private class KillRecord(string attacker, string target, int timestamp)
    {
        public string Attacker { get; set; } = attacker;
        public string Target { get; set; } = target;
        public int Timestamp { get; set; } = timestamp;
    }
}

public class TrackedRoom
{
    [JsonProperty("name")] public string Name { get; set; }

    [JsonProperty("shape")] public string Shape { get; set; }

    [JsonProperty("zone")] public string Zone { get; set; }

    [JsonProperty("pos")] public TrackedCoordinates Pos { get; set; }

    [JsonProperty("connectedRooms")] public List<TrackedConnectedRoom> ConnectedRooms { get; set; }
}

public class TrackedConnectedRoom
{
    [JsonProperty("pos")] public TrackedCoordinates Pos { get; set; }
}

public class TrackedCoordinates
{
    [JsonProperty("x")] public float X { get; set; }

    [JsonProperty("z")] public float Z { get; set; }
}