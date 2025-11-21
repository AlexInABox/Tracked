using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LabApi.Features.Wrappers;
using MEC;
using Newtonsoft.Json;
using Logger = LabApi.Features.Console.Logger;

namespace Tracked;

public static class Utils
{
    private static readonly Config Config = Plugin.Instance.Config!;
    public static readonly ConcurrentDictionary<string, int> RemoteZvcCount = new();


    public static IEnumerator<float> FetchAllZvcCoroutine()
    {
        while (true)
        {
            _ = FetchAllZvc();
            yield return Timing.WaitForSeconds(15f);
        }
        // ReSharper disable once IteratorNeverReturns
    }

    private static async Task FetchAllZvc()
    {
        List<string> userIds = Player.ReadyList.Where(p => !p.IsDummy && !p.IsHost).Select(p => p.UserId).ToList();
        if (userIds.Count == 0) return;

        try
        {
            // Build query string: ?userId=a&userId=b&userId=c
            string qs = string.Join("&",
                userIds.Select(id => $"userId={Uri.EscapeDataString(id)}"));

            Logger.Debug($"{Config.EndpointUrl}/public/zvc?{qs}",
                Plugin.Instance.Config!.Debug);

            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

            HttpResponseMessage response = await client
                .GetAsync($"{Config.EndpointUrl}/public/zvc?{qs}")
                .ConfigureAwait(false);

            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Logger.Debug($"Fetched ZVC array: {responseText}", Plugin.Instance.Config!.Debug);

            // Deserialize array
            List<ZvcResponse> data = JsonConvert.DeserializeObject<List<ZvcResponse>>(responseText)
                                     ?? [];

            // Fill dictionary; missing players → 0
            foreach (string uid in userIds) RemoteZvcCount[uid] = data.FirstOrDefault(x => x.userId == uid)?.zvc ?? 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to fetch ZVC list: {ex}");
        }
    }

    public static async Task FetchZvcForUser(string userId)
    {
        try
        {
            string qs = $"userId={Uri.EscapeDataString(userId)}";

            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", Config.Apikey);

            HttpResponseMessage response = await client
                .GetAsync($"{Config.EndpointUrl}/public/zvc?{qs}")
                .ConfigureAwait(false);

            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            ZvcResponse data = JsonConvert.DeserializeObject<List<ZvcResponse>>(responseText)?
                .FirstOrDefault(x => x.userId == userId);

            RemoteZvcCount[userId] = data?.zvc ?? 0;
        }
        catch
        {
            RemoteZvcCount[userId] = 0;
        }
    }


    private class ZvcResponse
    {
        public string userId { get; set; }
        public int zvc { get; set; }
    }
}