
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using HtmlAgilityPack;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace RustTwitchDrops
{
    class Program : ConsoleAppBase
    {
        static async Task Main(string[] args)
        {
            // target T as ConsoleAppBase.
            await Host.CreateDefaultBuilder().RunConsoleAppFrameworkAsync<Program>(args);
        }

        private static int cicle_time = 5 * 60;

        private List<WatchEntry> LiveStreamers;
        private WatchEntry CurrentStreamer;
        private AppDb Config;


        public Task Default()
        {
            Console.WriteLine($"Running default task");
            return Run(10, true);
        }

        private void UpdateTitle(string text)
        {
            if (string.IsNullOrEmpty(text))
                Console.Title = $"c:{cicle_time}s b:{_noBrowserActions}";
            else
                Console.Title = $"c:{cicle_time}s b:{_noBrowserActions} a:{text}";
        }

        private void Log(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{DateTime.Now.ToString("T", CultureInfo.InvariantCulture)}: ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        // allows void/Task return type, parameter is automatically binded from string[] args.

        [Command("run")]
        public async Task Run([Option(0)] int cycle, [Option(1)] bool noBrowser)
        {
            cicle_time = cycle;
            this._noBrowserActions = noBrowser;

            Console.WriteLine($"Cycle time: {cycle}s, NoBrowser - {noBrowser}");
            UpdateTitle(null);

            ReadConfig();
            ProcessAllStreamers();
            //ProcessCurrentLiveStreamers();

            // you can write infinite-loop while stop request(Ctrl+C or docker terminate).
            try
            {
                while (!this.Context.CancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (this.CurrentStreamer != null)
                        {
                            var left = this.CurrentStreamer.GetCurrentLeft();
                            var text =
                                $"Watching: {this.CurrentStreamer.TwitchUserName}, left - {(int)left.TotalMinutes}:{left.Seconds:00}";
                            Log(text);
                            if (left.TotalSeconds < 1)
                            {
                                Log($"Watching finished for: {this.CurrentStreamer.TwitchUserName}");
                                SaveProgress(this.CurrentStreamer);
                                this.CurrentStreamer = null;
                            }
                        }
                        else
                        {
                            //Console.WriteLine($"Watching: NONE");
                            Log($"Watching: NONE");
                        }

                        ProcessCurrentLiveStreamers();
                        var newTopDog = SelectStreamerToWatch();
                        if (newTopDog == null)
                        {
                            //Console.WriteLine($"No new Streamer wes chosen, skipping...");
                            Log($"No new Streamer wes chosen, skipping...");
                            goto toEnd;
                        }

                        if (newTopDog.TwitchUserName == this.CurrentStreamer?.TwitchUserName)
                        {
                            Log($"New Streamer is the Current streamer ({this.CurrentStreamer?.TwitchUserName}), skipping...");
                            goto toEnd;
                        }

                        ShutdownBrowser();
                        SaveProgress(this.CurrentStreamer);
                        Log($"New Streamer is ({newTopDog.TwitchUserName}), starting...");
                        StartWatching(newTopDog);
                        CurrentStreamer = newTopDog;
                    }
                    catch (Exception ex)
                    {
                        // error occured but continue to run(or terminate).
                        Console.WriteLine(ex.Message, "Found error");
                    }

                    // wait for next time
                    toEnd: await Task.Delay(TimeSpan.FromSeconds(cicle_time), this.Context.CancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // you can write finally exception handling(without cancellation)
            }
            finally
            {

                // you can write cleanup code here.
            }
        }

        private bool _noBrowserActions;

        private void SaveProgress(WatchEntry streamer)
        {
            if (streamer == null) return;
            streamer.Stop();
            this.UpdateConfig();
        }


        private void ShutdownBrowser()
        {
            if (_noBrowserActions)
                return;
            Process.Start("tskill", "msedge");
            Thread.Sleep(100);
        }

        private void StartWatching(WatchEntry newOne)
        {
            if (newOne == null) return;
            newOne.Start();

            if (_noBrowserActions)
                return;

            Process.Start(new ProcessStartInfo { FileName = newOne.TwitchUrl, UseShellExecute = true });
        }

        private void ProcessCurrentLiveStreamers()
        {
            var url = "https://twitch.facepunch.com/";
            var web = new HtmlWeb();
            var doc = web.Load(url);
            var streamersNodes = doc.DocumentNode.SelectNodes("//a[contains(@class,'drop is-live')]").ToArray();

            LiveStreamers = new List<WatchEntry>();

            foreach (var node in streamersNodes)
            {
                var twitchUrl = node.GetAttributeValue("href", null);

                if (twitchUrl == null)
                    continue;

                var streamerName = new Uri(twitchUrl.TrimEnd('/')).Segments.LastOrDefault();

                //var streamerItemNode = node.SelectSingleNode(".//span[contains(@class,'streamer-name')]");
                //if (streamerItemNode != null)
                //    streamerName = streamerItemNode.InnerText;

                if (streamerName == null)
                    continue;
                LiveStreamers.Add(new WatchEntry()
                {
                    TwitchUrl = twitchUrl,
                    TwitchUserName = streamerName
                });

                //Console.WriteLine($"{streamerName}:{twitchUrl}");
            }
        }

        private void ProcessAllStreamers()
        {
            var url = "https://twitch.facepunch.com/";
            var web = new HtmlWeb();
            var doc = web.Load(url);
            var streamersNodes = doc.DocumentNode.SelectNodes("//a[contains(@class,'drop')]").ToArray();

            var streamers = new List<WatchEntry>();

            foreach (var node in streamersNodes)
            {
                var twitchUrl = node.GetAttributeValue("href", null);

                if (twitchUrl == null)
                    continue;

                try
                {
                    var streamerName = new Uri(twitchUrl.TrimEnd('/')).Segments.LastOrDefault();
                    string timestr = null;
                    var streamerTimeNodeParent = node.SelectSingleNode(".//div[contains(@class,'drop-time')]");
                    var streamerTimeNode = streamerTimeNodeParent.SelectSingleNode(".//span");
                    if (streamerTimeNode != null)
                        timestr = streamerTimeNode.InnerText.Split(' ')[0];

                    if (streamerName == null)
                        continue;

                    if (timestr == null)
                        continue;

                    var time = (int)TimeSpan.FromHours(Int32.Parse(timestr)).Add(TimeSpan.FromMinutes(5)).TotalSeconds;
                    streamers.Add(new WatchEntry()
                    {
                        TwitchUrl = twitchUrl,
                        TwitchUserName = streamerName,
                        Left = time,
                    });

                    Log($"ALL STREAMERS: {streamerName} : {time} : {twitchUrl}");

                }
                catch (Exception e)
                {
                }
            }

            var db = new AppDb() { WatchList = streamers };

            var str = JsonConvert.SerializeObject(db, Formatting.Indented);
            File.WriteAllText("db.raw.json", str);
        }

        private WatchEntry SelectStreamerToWatch()
        {
            if ((this.LiveStreamers ?? new List<WatchEntry>(0)).Any())
            {
                var onlineUsersFromConfig = this.Config.WatchList
                    .Where(x => this.LiveStreamers.Any(live => live.TwitchUserName == x.TwitchUserName))
                    .ToArray();


                var offlineUsersFromConfig = this.Config.WatchList
                    .Where(x => this.LiveStreamers.All(live => live.TwitchUserName != x.TwitchUserName))
                    .ToArray();

                PrintUsers("Online", onlineUsersFromConfig);
                PrintUsers("Offline", offlineUsersFromConfig);

                void PrintUsers(string msg, IList<WatchEntry> users)
                {
                    var ss = users.OrderBy(x => x.Left).Select(x => new string[]
                      {
                        x.TwitchUserName,
                        " ",
                        WatchEntry.ToText(x.GetCurrentLeft()),
                        "; "
                      }).SelectMany(x => x).ToArray();

                    Log($"{msg}: {string.Join("", ss)}");
                }

                var top = onlineUsersFromConfig
                    .Where(x => x.Left > 0)
                    .OrderBy(x => x.Left)
                    .FirstOrDefault();

                return top;
            }
            return null;
        }

        private void ReadConfig()
        {
            if (!File.Exists("db.json"))
            {
                Config = new AppDb();
                Config.WatchList.Add(new WatchEntry() { });
                UpdateConfig();
            }

            var file = File.ReadAllText("db.json");
            this.Config = JsonConvert.DeserializeObject<AppDb>(file);
        }

        private void UpdateConfig()
        {
            var str = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText("db.json", str);
        }
    }

    public class WatchEntry
    {
        public string TwitchUrl { get; set; }
        public string TwitchUserName { get; set; }
        public int Left { get; set; }
        private Stopwatch _counter;
        public void Start()
        {
            _counter = Stopwatch.StartNew();
        }

        public void Stop()
        {
            _counter.Stop();

            var left = GetCurrentLeft();
            Left = (int)left.TotalSeconds;
            _counter = null;
        }

        public TimeSpan GetCurrentLeft()
        {
            if (_counter == null)
                return TimeSpan.FromSeconds(Left);

            var left = TimeSpan.FromSeconds(Left);
            left = left.Subtract(TimeSpan.FromMilliseconds(_counter.ElapsedMilliseconds));

            if (left.TotalSeconds < 0)
                return TimeSpan.Zero;

            return left;
        }

        public static string ToText(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        }
    }

    public class AppDb
    {
        public AppDb()
        {
            WatchList = new List<WatchEntry>();
        }
        public List<WatchEntry> WatchList { get; set; }
    }
}
