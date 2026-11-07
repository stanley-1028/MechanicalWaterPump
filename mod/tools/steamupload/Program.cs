using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Steamworks;

// Minimal Steam Workshop publisher for the Mechanical Water Pump mod.
// It mirrors the official in-game uploader: content folder + title/description/preview/tags come
// from the package itself (manifest.json + workshop_data.json + thumbnail.png).
internal static class Program
{
    private const uint AppIdValue = 1062090; // Timberborn

    private static int Main(string[] args)
    {
        EnsureAppIdFile();

        if (args.Length == 1 && args[0] == "--probe")
        {
            return Probe();
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: SteamUpload --probe | SteamUpload <contentFolder> <changeNoteFile> [itemId]");
            return 1;
        }

        string content = Path.GetFullPath(args[0]);
        string note = File.ReadAllText(Path.GetFullPath(args[1]), Encoding.UTF8).Trim();
        if (!Directory.Exists(content))
        {
            Console.Error.WriteLine("content folder missing: " + content);
            return 1;
        }

        string manifestPath = Path.Combine(content, "manifest.json");
        string workshopDataPath = Path.Combine(content, "workshop_data.json");
        if (!File.Exists(manifestPath) || !File.Exists(workshopDataPath))
        {
            Console.Error.WriteLine("manifest.json / workshop_data.json missing in content folder");
            return 1;
        }

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        using JsonDocument workshop = JsonDocument.Parse(File.ReadAllText(workshopDataPath, Encoding.UTF8));
        string title = workshop.RootElement.TryGetProperty("Name", out JsonElement n)
            ? n.GetString()
            : manifest.RootElement.GetProperty("Name").GetString();
        string description = manifest.RootElement.GetProperty("Description").GetString();
        var tags = new List<string>();
        if (workshop.RootElement.TryGetProperty("Tags", out JsonElement tagArray))
        {
            foreach (JsonElement tag in tagArray.EnumerateArray())
            {
                tags.Add(tag.GetString());
            }
        }

        ulong itemId = 0;
        if (args.Length >= 3 && ulong.TryParse(args[2], out ulong argId))
        {
            itemId = argId;
        }
        else if (workshop.RootElement.TryGetProperty("ItemId", out JsonElement idElement)
                 && ulong.TryParse(idElement.GetString(), out ulong parsedId))
        {
            itemId = parsedId;
        }

        ERemoteStoragePublishedFileVisibility visibility = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
        string visibilityName = workshop.RootElement.TryGetProperty("Visibility", out JsonElement v)
            ? v.GetString()
            : "Public";
        if (string.Equals(visibilityName, "Private", StringComparison.OrdinalIgnoreCase))
        {
            visibility = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate;
        }
        else if (string.Equals(visibilityName, "Unlisted", StringComparison.OrdinalIgnoreCase))
        {
            visibility = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted;
        }
        else if (string.Equals(visibilityName, "Friends", StringComparison.OrdinalIgnoreCase))
        {
            visibility = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly;
        }

        if (!SteamAPI.Init())
        {
            SteamAPI.InitEx(out string initError);
            Console.Error.WriteLine("SteamAPI.Init failed: " + initError);
            return 2;
        }

        try
        {
            Console.WriteLine("loggedOn=" + SteamUser.BLoggedOn() + " steamId=" + SteamUser.GetSteamID());

            if (itemId == 0)
            {
                Console.WriteLine("no ItemId supplied -> creating a new Workshop item");
                itemId = CreateItem();
                if (itemId == 0)
                {
                    return 8;
                }
                Console.WriteLine("ITEM_ID=" + itemId);
            }

            var handle = SteamUGC.StartItemUpdate(new AppId_t(AppIdValue), new PublishedFileId_t(itemId));
            if (handle == UGCUpdateHandle_t.Invalid)
            {
                Console.Error.WriteLine("StartItemUpdate returned an invalid handle for item " + itemId);
                return 3;
            }

            if (!SteamUGC.SetItemContent(handle, content)) { Console.Error.WriteLine("SetItemContent failed"); return 4; }
            if (!SteamUGC.SetItemTitle(handle, title)) { Console.Error.WriteLine("SetItemTitle failed"); return 4; }
            if (!SteamUGC.SetItemDescription(handle, description)) { Console.Error.WriteLine("SetItemDescription failed"); return 4; }
            string preview = Path.Combine(content, "thumbnail.png");
            if (File.Exists(preview) && !SteamUGC.SetItemPreview(handle, preview))
            {
                Console.Error.WriteLine("SetItemPreview failed");
                return 4;
            }
            if (tags.Count > 0 && !SteamUGC.SetItemTags(handle, tags, false))
            {
                Console.Error.WriteLine("SetItemTags failed");
                return 4;
            }
            if (!SteamUGC.SetItemVisibility(handle, visibility))
            {
                Console.Error.WriteLine("SetItemVisibility failed");
                return 4;
            }

            Console.WriteLine("content=" + content);
            Console.WriteLine("title=" + title);
            Console.WriteLine("tags=" + string.Join(",", tags));

            var call = SteamUGC.SubmitItemUpdate(handle, note);
            if (call == SteamAPICall_t.Invalid)
            {
                Console.Error.WriteLine("SubmitItemUpdate returned an invalid call");
                return 5;
            }

            bool done = false;
            bool ioFailure = false;
            SubmitItemUpdateResult_t result = default;
            var callResult = CallResult<SubmitItemUpdateResult_t>.Create((r, bIO) => { result = r; ioFailure = bIO; done = true; });
            callResult.Set(call);

            DateTime deadline = DateTime.UtcNow.AddMinutes(30);
            EItemUpdateStatus lastStatus = EItemUpdateStatus.k_EItemUpdateStatusInvalid;
            while (!done && DateTime.UtcNow < deadline)
            {
                SteamAPI.RunCallbacks();
                EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(handle, out ulong processed, out ulong total);
                if (status != lastStatus || (total > 0 && processed == total))
                {
                    Console.WriteLine("status=" + status + " " + processed + "/" + total);
                    lastStatus = status;
                }
                Thread.Sleep(200);
            }

            callResult.Dispose();
            if (!done)
            {
                Console.Error.WriteLine("timeout waiting for SubmitItemUpdate result");
                return 6;
            }

            Console.WriteLine("submitDone ioFailure=" + ioFailure + " eResult=" + result.m_eResult
                + " needsLegalAgreement=" + result.m_bUserNeedsToAcceptWorkshopLegalAgreement
                + " publishedFileId=" + result.m_nPublishedFileId);
            Console.WriteLine("ITEM_ID=" + result.m_nPublishedFileId);
            return (result.m_eResult == EResult.k_EResultOK && !ioFailure) ? 0 : 7;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static ulong CreateItem()
    {
        bool done = false;
        bool ioFailure = false;
        CreateItemResult_t result = default;
        var callResult = CallResult<CreateItemResult_t>.Create((r, bIO) => { result = r; ioFailure = bIO; done = true; });
        var call = SteamUGC.CreateItem(new AppId_t(AppIdValue), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
        callResult.Set(call);

        DateTime deadline = DateTime.UtcNow.AddMinutes(2);
        while (!done && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(100);
        }

        callResult.Dispose();
        if (!done || ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            Console.Error.WriteLine("CreateItem failed: done=" + done + " io=" + ioFailure + " eResult=" + result.m_eResult);
            return 0;
        }

        Console.WriteLine("createdItem=" + result.m_nPublishedFileId + " needsLegalAgreement=" + result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            Console.Error.WriteLine("The Steam account must accept the Steam Workshop legal agreement once: open the item page / Steam Workshop agreement and rerun this tool.");
        }
        return result.m_nPublishedFileId.m_PublishedFileId;
    }

    private static int Probe()
    {
        if (!SteamAPI.Init())
        {
            SteamAPI.InitEx(out string err);
            Console.Error.WriteLine("SteamAPI.Init failed: " + err);
            return 2;
        }

        try
        {
            Console.WriteLine("steamRunning=" + SteamAPI.IsSteamRunning()
                + " loggedOn=" + SteamUser.BLoggedOn()
                + " steamId=" + SteamUser.GetSteamID());
            return 0;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static void EnsureAppIdFile()
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt"), AppIdValue.ToString());
        }
        catch (IOException)
        {
            // The file may already exist and be locked by another process; the app id is set anyway.
        }
    }
}
