﻿namespace iPhoneController.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using DSharpPlus.CommandsNext;
    using DSharpPlus.CommandsNext.Attributes;
    using DSharpPlus.Entities;

    using iPhoneController.Data;
    using iPhoneController.Diagnostics;
    using iPhoneController.Utils;

    using Microsoft.EntityFrameworkCore;

    //TODO: Restart all devices

    public class PhoneControl
    {
        private static readonly IEventLogger _logger = EventLogger.GetLogger("PHONE_CTRL");
        private readonly Dependencies _dep;

        #region Constructor

        public PhoneControl(Dependencies dep)
        {
            _dep = dep;
        }

        #endregion

        [
            Command("list"),
            Description("List all available devices.")
        ]
        public async Task ListDevicesAsync(CommandContext ctx,
            [Description(""), RemainingText] string machineName = "")
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            if (!string.IsNullOrEmpty(machineName) && !(string.Compare(machineName, Environment.MachineName, true) == 0))
                return;

            var realDevices = await GetDevices();
            var devices = realDevices.Keys.ToList();
            var devicesString = string.Empty;
            for (var i = 0; i < devices.Count; i++)
            {
                var name = devices[i];
                var uuid = realDevices[name];
                devicesString += $"**{name}**: {uuid}\r\n";
            }

            var eb = new DiscordEmbedBuilder
            {
                Description = devicesString,
                Title = $"{Environment.MachineName} Device List ({devices.Count.ToString("N0")})",
                Color = DiscordColor.Blurple
            };
            await ctx.RespondAsync(embed: eb);
        }

        [
            Command("reboot"),
            Description("Reboot specific device(s).")
        ]
        public async Task RebootAsync(CommandContext ctx,
            [Description("iPhone names i.e. `iPhoneHV1SE`. Comma delimiter supported `iPhoneHV1SE,iPhoneHV2SE`"), RemainingText]
            string phoneNames)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            //TODO: Check if idevicediagnostics is installed.

            var realDevices = await GetDevices();
            var devices = phoneNames.Replace(", ", ",").Split(',');
            for (var i = 0; i < devices.Length; i++)
            {
                var name = devices[i];
                if (!realDevices.ContainsKey(name))
                {
                    _logger.Warn($"{name} does not exist in device list, skipping reboot.");
                    continue;
                }

                var uuid = realDevices[name];
                var output = Shell.Execute("idevicediagnostics", $"-u {uuid} restart", out var exitCode);
                var message = exitCode == 0 ? $"Restarting device {name} ({uuid})" : output;
                await ctx.RespondAsync(message);
            }
        }

        [
            Command("screen"),
            Description("Grab a screenshot of a specific device.")
        ]
        public async Task ScreenshotAsync(CommandContext ctx,
            [Description("iPhone names i.e. `iPhoneAB1SE`. Comma delimiter supported `iPhoneAB1SE,iPhoneCD2SE`"), RemainingText]
            string phoneNames)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            //TODO: Check if idevicescreenshot is installed.

            var realDevices = await GetDevices();
            var devices = phoneNames.Replace(", ", ",").Split(',');
            var devicesFailed = new Dictionary<string, string>();
            for (var i = 0; i < devices.Length; i++)
            {
                var name = devices[i];
                if (!realDevices.ContainsKey(name))
                {
                    _logger.Warn($"{name} does not exist in device list, skipping screenshot.");
                    continue;
                }

                var uuid = realDevices[name];
                var fileName = $"{uuid}.jpg";
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                var output = Shell.Execute("idevicescreenshot", $"-u {uuid} {fileName}", out var exitCode);
                if (exitCode == 0)
                {
                    //var message = exitCode == 0 ? $"Restarting device {name} ({uuid})" : output;
                    await ctx.RespondWithFileAsync(fileName, $"Screenshot for device **{name}** ({uuid})");
                    continue;
                }

                devicesFailed.Add(name, output);
            }

            if (devicesFailed.Count > 0)
            {
                var eb = new DiscordEmbedBuilder
                {
                    Color = DiscordColor.Red,
                    Title = "Screenshots failed on the following devices",
                    Description = string.Join("\r\n", devicesFailed.Select(x => $"- **{x.Key}**: {x.Value}"))
                };
                await ctx.RespondAsync(embed: eb);
            }
        }

        [
            Command("rm-uic"),
            Description("Remove UI-Controller from device(s).")
        ]
        public async Task RemoveUICAsync(CommandContext ctx,
            [Description("iPhone names i.e. `iPhoneAB1SE`. Comma delimiter supported `iPhoneAB1SE,iPhoneCD2SE`"), RemainingText]
            string phoneNames)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            var realDevices = await GetDevices();
            var devices = phoneNames.Replace(", ", ",").Split(',');
            for (var i = 0; i < devices.Length; i++)
            {
                var name = devices[i];
                if (!realDevices.ContainsKey(name))
                {
                    _logger.Warn($"{name} does not exist in device list, skipping remove uic.");
                    continue;
                }

                var uuid = realDevices[name];
                var args = $"--id {uuid} --uninstall_only --bundle_id com.apple.test.RealDeviceMap-UIControlUITests-Runner";
                var output = Shell.Execute("ios-deploy", args, out var exitCode);
                await ctx.RespondAsync($"Removed UIC from {name}\r\nOutput: {output}");
            }
        }

        [
            Command("rm-pogo"),
            Description("Remove Pokemon Go application from device(s).")
        ]
        public async Task RemovePoGoAsync(CommandContext ctx,
            [Description("iPhone names i.e. `iPhoneAB1SE`. Comma delimiter supported `iPhoneAB1SE,iPhoneCD2SE`"), RemainingText]
            string phoneNames)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            var realDevices = await GetDevices();
            var devices = phoneNames.Replace(", ", ",").Split(',');
            for (var i = 0; i < devices.Length; i++)
            {
                var name = devices[i];
                if (!realDevices.ContainsKey(name))
                {
                    _logger.Warn($"{name} does not exist in device list, skipping remove pogo.");
                    continue;
                }

                var uuid = realDevices[name];
                var args = $"--id {uuid} --uninstall_only --bundle_id com.nianticlabs.pokemongo";
                var output = Shell.Execute("ios-deploy", args, out var exitCode);
                await ctx.RespondAsync($"Removed Pokemon Go from {name}\r\nOutput: {output}");
            }
        }

        [
            Command("log-full"),
            Description("")
        ]
        public async Task FullLogAsync(CommandContext ctx,
            [Description(""), RemainingText]
            string phoneName)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            var realDevices = await GetDevices();
            if (!realDevices.ContainsKey(phoneName))
            {
                _logger.Warn($"{phoneName} does not exist in device list, skipping full log.");
                return;
            }

            var output = GetLatestLog(phoneName, true);
            output = string.IsNullOrEmpty(output) ? $"Failed to get latest full log for device {phoneName}." : output;
            if (File.Exists(output))
                output = File.ReadAllText(output);

            if (output.Length > 1500)
                output = output.Substring(output.Length - 1494, 1494);

            await ctx.RespondAsync($"```{output}```");
        }

        [
            Command("log-debug"),
            Description("")
        ]
        public async Task DebugLogAsync(CommandContext ctx,
            [Description(""), RemainingText]
            string phoneName)
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            var realDevices = await GetDevices();
            if (!realDevices.ContainsKey(phoneName))
            {
                _logger.Warn($"{phoneName} does not exist in device list, skipping full log.");
                return;
            }

            var output = GetLatestLog(phoneName, false);
            output = string.IsNullOrEmpty(output) ? $"Failed to get latest debug log for device {phoneName}." : output;
            if (File.Exists(output))
                output = File.ReadAllText(output);

            if (output.Length > 1500)
                output = output.Substring(output.Length - 1494, 1494);

            await ctx.RespondAsync($"```{output}```");
        }

        [
            Command("log-clear"),
            Description("")
        ]
        public async Task ClearLogsAsync(CommandContext ctx,
            [Description(""), RemainingText] string machineName = "")
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            if (!string.IsNullOrEmpty(machineName) && !(string.Compare(machineName, Environment.MachineName, true) == 0))
                return;

            var managerFolder = Path.GetDirectoryName(_dep.Config.SQLiteFilePath);
            var logsFolder = Path.Combine(managerFolder, "Logs");
            var logFiles = Directory.GetFiles(logsFolder, "*.log");
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (var i = 0; i < logFiles.Length; i++)
            {
                File.Delete(logFiles[i]);
            }
            sw.Stop();
            await ctx.RespondAsync($"All log files deleted for {Environment.MachineName} (took {sw.Elapsed}).");
        }

        [
            Command("kill"),
            Description("")
        ]
        public async Task KillAsync(CommandContext ctx,
            [Description(""), RemainingText] string processName,
            [Description(""), RemainingText] string machineName = "")
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            if (!string.IsNullOrEmpty(machineName) && !(string.Compare(machineName, Environment.MachineName, true) == 0))
                return;

            var output = Shell.Execute("killall", processName, out var exitCode);
            await ctx.RespondAsync(exitCode == 0 ? $"{processName} killed." : output);
        }

        [
            Command("iosver"),
            Description("")
        ]
        public async Task IosVersionAsync(CommandContext ctx,
            [Description(""), RemainingText] string machineName = "")
        {
            if (!HasRequiredRoles(ctx.Member))
                await ctx.RespondAsync($":no_entry: {ctx.User.Username} Unauthorized permissions.");

            if (!IsValidChannel(ctx.Channel.Id))
                await ctx.RespondAsync($":warning: {ctx.User.Username} Invalid channel.");

            if (!string.IsNullOrEmpty(machineName) && !(string.Compare(machineName, Environment.MachineName, true) == 0))
                return;

            var dict = new Dictionary<string, string>();
            var realDevices = await GetDevices();
            var keys = realDevices.Keys.ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                var name = keys[i];
                var uuid = realDevices[name];
                var args = $"-u {uuid} | grep \"ProductVersion:\"";
                var output = Shell.Execute("ideviceinfo", args, out var exitCode);
                if (exitCode != 0)
                {
                    _logger.Warn($"Failed to get device info from {name} ({uuid}).");
                    continue;
                }
                if (dict.ContainsKey(name))
                {
                    _logger.Warn($"Duplicate device {name} ({uuid}).");
                    continue;
                }
                dict.Add(name, output);
            }

            if (dict.Count == 0)
            {
                await ctx.RespondAsync($"Failed to get device info from any devices.");
                return;
            }

            var eb = new DiscordEmbedBuilder
            {
                Color = DiscordColor.Blurple,
                Title = $"**{Environment.MachineName}** Device iOS Versions",
                Description = string.Join("\r\n", dict.Select(x => $"- **{x.Key}**: {x.Value.Replace("ProductVersion: ", null)}"))
            };
            await ctx.RespondAsync(embed: eb);
        }

        #region Private Methods

        private bool HasRequiredRoles(DiscordMember member)
        {
            var requiredRoles = _dep.Config.RequiredRoles;
            if (requiredRoles.Count == 0)
                return true;

            var memberRoles = member.Roles?.Select(x => x.Id)?.ToList();
            if (memberRoles == null)
                return false;

            for (var i = 0; i < requiredRoles.Count; i++)
            {
                var requiredRole = requiredRoles[i];
                if (memberRoles.Contains(requiredRole))
                    return true;
            }

            return false;
        }

        private bool IsValidChannel(ulong channelId)
        {
            return _dep.Config.RequiredRoles.Count == 0 || _dep.Config.RequiredRoles.Contains(channelId);
        }

        private async Task<Dictionary<string, string>> GetDevices()
        {
            var sqliteFilePath = _dep.Config.SQLiteFilePath;
            if (!File.Exists(sqliteFilePath))
            {
                _logger.Warn($"Failed to get device list, {sqliteFilePath} does not exist.");
                return null;
            }

            using (var db = DbContextFactory.Create($"Data Source={sqliteFilePath};"))
            {
                var devices = await db.Devices
                    .Where(x => x.Uuid != "default")
                    .ToListAsync();

                if (devices?.Count == 0)
                {
                    _logger.Warn($"No devices in sqlite database.");
                    return null;
                }

                return devices.ToDictionary(x => x.Name, y => y.Uuid);
            }
        }

        private string GetLatestLog(string deviceName, bool full)
        {
            var managerFolder = Path.GetDirectoryName(_dep.Config.SQLiteFilePath);
            var logsFolder = Path.Combine(managerFolder, "Logs");
            var logs = Directory.GetFiles(logsFolder, $"*{deviceName}{(full ? "*.full.log" : "*.debug.log")}");
            var logFile = logs.FirstOrDefault(x =>
                Math.Round(DateTime.Now.Subtract(File.GetLastWriteTime(x)).TotalMinutes) <= 60); //Log was within the last hour, TODO: Change to 1-5 minutes, maybe configurable?
            return logFile;
        }

        #endregion
    }

    public class PhoneManager
    {
        private static readonly IEventLogger _logger = EventLogger.GetLogger("PHONE_MGR");

        private readonly Dictionary<string, string> _devices;

        public IReadOnlyDictionary<string, string> Devices => _devices;

        public PhoneManager()
        {
            _devices = new Dictionary<string, string>();
        }

        public bool RestartDevice(string name)
        {
            return true;
        }

        public string Screenshot(string name)
        {
            return string.Empty;
        }
    }
}