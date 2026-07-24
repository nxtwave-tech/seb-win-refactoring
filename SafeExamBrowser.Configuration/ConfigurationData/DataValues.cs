/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using SafeExamBrowser.Configuration.Contracts;
using SafeExamBrowser.Settings;
using SafeExamBrowser.Settings.Applications;
using SafeExamBrowser.Settings.Browser;
using SafeExamBrowser.Settings.Browser.Proxy;
using SafeExamBrowser.Settings.Logging;
using SafeExamBrowser.Settings.Proctoring;
using SafeExamBrowser.Settings.Security;
using SafeExamBrowser.Settings.Service;
using SafeExamBrowser.Settings.UserInterface;

namespace SafeExamBrowser.Configuration.ConfigurationData
{
	internal class DataValues
	{
		private const string DEFAULT_CONFIGURATION_NAME = "TsbClientSettings.tsb";
		private AppConfig appConfig;

		internal string GetAppDataFilePath()
		{
			return appConfig.AppDataFilePath;
		}

		internal AppConfig InitializeAppConfig()
		{
			var executable = Assembly.GetEntryAssembly();
			var certificate = executable.Modules.First().GetSignerCertificate();
			var programBuild = FileVersionInfo.GetVersionInfo(executable.Location).FileVersion;
			var programCopyright = executable.GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;
			var programTitle = executable.GetCustomAttribute<AssemblyTitleAttribute>().Title;
			var programVersion = executable.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
			var appDataLocalFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TopinSecureBrowser");
			var appDataRoamingFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TopinSecureBrowser");
			var programDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TopinSecureBrowser");
			var temporaryFolder = Path.Combine(appDataLocalFolder, "Temp");
			var startTime = DateTime.Now;
			var logFolder = Path.Combine(appDataLocalFolder, "Logs");
			var logFilePrefix = startTime.ToString("yyyy-MM-dd\\_HH\\hmm\\mss\\s");

			appConfig = new AppConfig();
			appConfig.AppDataFilePath = Path.Combine(appDataRoamingFolder, DEFAULT_CONFIGURATION_NAME);
			appConfig.ApplicationStartTime = startTime;
			appConfig.BrowserCachePath = Path.Combine(appDataLocalFolder, "Cache");
			appConfig.BrowserLogFilePath = Path.Combine(logFolder, $"{logFilePrefix}_Browser.log");
			appConfig.ClientId = Guid.NewGuid();
			appConfig.ClientAddress = $"{AppConfig.BASE_ADDRESS}/client/{Guid.NewGuid()}";
			appConfig.ClientExecutablePath = Path.Combine(Path.GetDirectoryName(executable.Location), "SafeExamBrowser.Client.exe");
			appConfig.ClientLogFilePath = Path.Combine(logFolder, $"{logFilePrefix}_Client.log");
			appConfig.CodeSignatureHash = certificate?.GetCertHashString();
			appConfig.ConfigurationFileExtension = ".tsb";
			appConfig.ConfigurationFileMimeType = "application/tsb";
			appConfig.ProgramBuildVersion = programBuild;
			appConfig.ProgramCopyright = programCopyright;
			appConfig.ProgramDataFilePath = Path.Combine(programDataFolder, DEFAULT_CONFIGURATION_NAME);
			appConfig.ProgramTitle = programTitle;
			appConfig.ProgramInformationalVersion = programVersion;
			appConfig.RuntimeId = Guid.NewGuid();
			appConfig.RuntimeAddress = $"{AppConfig.BASE_ADDRESS}/runtime/{Guid.NewGuid()}";
			appConfig.RuntimeLogFilePath = Path.Combine(logFolder, $"{logFilePrefix}_Runtime.log");
			appConfig.SebUriScheme = "tsb";
			appConfig.SebUriSchemeSecure = "tsbs";
			appConfig.ServiceAddress = $"{AppConfig.BASE_ADDRESS}/service";
			appConfig.ServiceEventName = $@"Global\TopinSecureBrowser-{Guid.NewGuid()}";
			appConfig.ServiceLogFilePath = Path.Combine(logFolder, $"{logFilePrefix}_Service.log");
			appConfig.SessionCacheFilePath = Path.Combine(temporaryFolder, "cache.bin");
			appConfig.TemporaryDirectory = temporaryFolder;

			return appConfig;
		}

		internal SessionConfiguration InitializeSessionConfiguration()
		{
			var configuration = new SessionConfiguration();

			appConfig.ClientId = Guid.NewGuid();
			appConfig.ClientAddress = $"{AppConfig.BASE_ADDRESS}/client/{Guid.NewGuid()}";
			appConfig.ServiceEventName = $@"Global\{nameof(SafeExamBrowser)}-{Guid.NewGuid()}";

			configuration.AppConfig = appConfig.Clone();
			configuration.ClientAuthenticationToken = Guid.NewGuid();
			configuration.SessionId = Guid.NewGuid();

			return configuration;
		}

		internal AppSettings LoadDefaultSettings()
		{
			var settings = new AppSettings();

			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "AA_v3.exe", OriginalName = "AA_v3.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "action.exe", OriginalName = "action.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "AeroAdmin.exe", OriginalName = "AeroAdmin.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "anydesk.exe", OriginalName = "anydesk.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ApowerMirror.exe", OriginalName = "ApowerMirror.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ApowerREC.exe", OriginalName = "ApowerREC.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "APPServerClient.exe", OriginalName = "APPServerClient.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "bdcam.exe", OriginalName = "bdcam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "beamyourscreen-host.exe", OriginalName = "beamyourscreen-host.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Braina.exe", OriginalName = "Braina.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "brave.exe", OriginalName = "brave.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "calculator.exe", OriginalName = "calculator.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CamoStudio.exe", OriginalName = "CamoStudio.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CamPlay.exe", OriginalName = "CamPlay.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CamRecorder.exe", OriginalName = "CamRecorder.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Camtasia.exe", OriginalName = "Camtasia.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Camtasia_Studio.exe", OriginalName = "Camtasia_Studio.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CamtasiaStudio.exe", OriginalName = "CamtasiaStudio.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CamtasiaUtl.exe", OriginalName = "CamtasiaUtl.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ccleaner.exe", OriginalName = "ccleaner.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CCleaner64.exe", OriginalName = "CCleaner64.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ChatGPT.exe", OriginalName = "ChatGPT.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "chrome.exe", OriginalName = "chrome.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "chromoting.exe", OriginalName = "chromoting.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CiscoCollabHost.exe", OriginalName = "CiscoCollabHost.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "CiscoWebExStart.exe", OriginalName = "CiscoWebExStart.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Claude.exe", OriginalName = "Claude.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ConnectWiseControl.Client.exe", OriginalName = "ConnectWiseControl.Client.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "copilot.exe", OriginalName = "copilot.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Deskin.exe", OriginalName = "Deskin.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Discord.exe", OriginalName = "Discord.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "DiscordCanary.exe", OriginalName = "DiscordCanary.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "DiscordPTB.exe", OriginalName = "DiscordPTB.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "DroidCam.exe", OriginalName = "DroidCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "DWAgent.exe", OriginalName = "DWAgent.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "DWAgentService.exe", OriginalName = "DWAgentService.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Element.exe", OriginalName = "Element.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "EpocCamService.exe", OriginalName = "EpocCamService.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "everything.exe", OriginalName = "everything.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "excel.exe", OriginalName = "excel.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "FineCam.exe", OriginalName = "FineCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "firefox.exe", OriginalName = "firefox.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "FlashBackRecorder.exe", OriginalName = "FlashBackRecorder.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "g2mcomm.exe", OriginalName = "g2mcomm.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "g2mlauncher.exe", OriginalName = "g2mlauncher.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "g2mstart.exe", OriginalName = "g2mstart.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "GameBar.exe", OriginalName = "GameBar.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "GameBarFTServer.exe", OriginalName = "GameBarFTServer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "GeForceExperience.exe", OriginalName = "GeForceExperience.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "gotomeeting.exe", OriginalName = "gotomeeting.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "GotoMeetingWinStore.exe", OriginalName = "GotoMeetingWinStore.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "greenshot.exe", OriginalName = "greenshot.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Guilded.exe", OriginalName = "Guilded.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Icecream Screen Recorder.exe", OriginalName = "Icecream Screen Recorder.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "IriunWebcam.exe", OriginalName = "IriunWebcam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "iVCam.exe", OriginalName = "iVCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "join.me.exe", OriginalName = "join.me.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "join.me.sentinel.exe", OriginalName = "join.me.sentinel.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "joinme.exe", OriginalName = "joinme.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Krisp.exe", OriginalName = "Krisp.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "LetsView.exe", OriginalName = "LetsView.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "lightshot.exe", OriginalName = "lightshot.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "LogiCapture.exe", OriginalName = "LogiCapture.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "logmein.exe", OriginalName = "logmein.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Loom.exe", OriginalName = "Loom.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "lync.exe", OriginalName = "lync.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "M365Copilot.exe", OriginalName = "M365Copilot.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ManyCam.exe", OriginalName = "ManyCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Microsoft.Copilot.exe", OriginalName = "Microsoft.Copilot.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Microsoft.Media.Player.exe", OriginalName = "Microsoft.Media.Player.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Mikogo-host.exe", OriginalName = "Mikogo-host.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Moonlight.exe", OriginalName = "Moonlight.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "MovaviScreenRecorder.exe", OriginalName = "MovaviScreenRecorder.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "MS-Teams.exe", OriginalName = "MS-Teams.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "msedge.exe", OriginalName = "msedge.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "msedgewebview2.exe", OriginalName = "msedgewebview2.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "mstsc.exe", OriginalName = "mstsc.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "NoMachine.exe", OriginalName = "NoMachine.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "notepad.exe", OriginalName = "notepad.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "NVIDIA Broadcast.exe", OriginalName = "NVIDIA Broadcast.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "NVIDIA Share.exe", OriginalName = "NVIDIA Share.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "nxplayer.exe", OriginalName = "nxplayer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "obs.exe", OriginalName = "obs.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "obs32.exe", OriginalName = "obs32.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "obs64.exe", OriginalName = "obs64.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "onenote.exe", OriginalName = "onenote.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "opera.exe", OriginalName = "opera.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "parallels.exe", OriginalName = "parallels.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Parsec.exe", OriginalName = "Parsec.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "parsecd.exe", OriginalName = "parsecd.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "PCMonitorSrv.exe", OriginalName = "PCMonitorSrv.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "pcmontask.exe", OriginalName = "pcmontask.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "PhoneCam.exe", OriginalName = "PhoneCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Poe.exe", OriginalName = "Poe.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "powerpnt.exe", OriginalName = "powerpnt.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ptoneclk.exe", OriginalName = "ptoneclk.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "pwsh.exe", OriginalName = "pwsh.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "PyGPT.exe", OriginalName = "PyGPT.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "qemu.exe", OriginalName = "qemu.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "rdpclip.exe", OriginalName = "rdpclip.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "regedit.exe", OriginalName = "regedit.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RemotePCDesktop.exe", OriginalName = "RemotePCDesktop.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RemotePCViewer.exe", OriginalName = "RemotePCViewer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "remoting_host.exe", OriginalName = "remoting_host.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RPCService.exe", OriginalName = "RPCService.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RPCSuite.exe", OriginalName = "RPCSuite.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RustDesk.exe", OriginalName = "RustDesk.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "RustDeskService.exe", OriginalName = "RustDeskService.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ScreenConnect.Client.exe", OriginalName = "ScreenConnect.Client.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "sethc.exe", OriginalName = "sethc.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "sharex.exe", OriginalName = "sharex.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Skype.exe", OriginalName = "Skype.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SkypeApp.exe", OriginalName = "SkypeApp.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SkypeHost.exe", OriginalName = "SkypeHost.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "slack.exe", OriginalName = "slack.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SmartConnect.exe", OriginalName = "SmartConnect.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "snagit.exe", OriginalName = "snagit.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SnapCamera.exe", OriginalName = "SnapCamera.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "splashtop.exe", OriginalName = "splashtop.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SplitCam.exe", OriginalName = "SplitCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "spotify.exe", OriginalName = "spotify.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "SRServer.exe", OriginalName = "SRServer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Steam.exe", OriginalName = "Steam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "steamservice.exe", OriginalName = "steamservice.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "steamwebhelper.exe", OriginalName = "steamwebhelper.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Streamlabs OBS.exe", OriginalName = "Streamlabs OBS.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "strwinclt.exe", OriginalName = "strwinclt.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Sunshine.exe", OriginalName = "Sunshine.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Supremo.exe", OriginalName = "Supremo.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "taskmgr.exe", OriginalName = "taskmgr.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Teams.exe", OriginalName = "Teams.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "TeamViewer.exe", OriginalName = "TeamViewer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Telegram.exe", OriginalName = "Telegram.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "tightvnc.exe", OriginalName = "tightvnc.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "TSClient.exe", OriginalName = "TSClient.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "UltraViewer_Desktop.exe", OriginalName = "UltraViewer_Desktop.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "UltraViewer_Service.exe", OriginalName = "UltraViewer_Service.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ultravnc.exe", OriginalName = "ultravnc.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vboxheadless.exe", OriginalName = "vboxheadless.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "virtualbox.exe", OriginalName = "virtualbox.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "VirtualBoxVM.exe", OriginalName = "VirtualBoxVM.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vlc.exe", OriginalName = "vlc.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vmplayer.exe", OriginalName = "vmplayer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vmware-vmx.exe", OriginalName = "vmware-vmx.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vmware.exe", OriginalName = "vmware.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vncserver.exe", OriginalName = "vncserver.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vncserverui.exe", OriginalName = "vncserverui.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vncviewer.exe", OriginalName = "vncviewer.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "voicemeeter8x64.exe", OriginalName = "voicemeeter8x64.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "vysor.exe", OriginalName = "vysor.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "webex.exe", OriginalName = "webex.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "webexmta.exe", OriginalName = "webexmta.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "whatsapp.exe", OriginalName = "whatsapp.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "WindowsCopilotRuntime.exe", OriginalName = "WindowsCopilotRuntime.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "winword.exe", OriginalName = "winword.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "XSplit.Core.exe", OriginalName = "XSplit.Core.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "XSplitVCam.exe", OriginalName = "XSplitVCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "yandex.exe", OriginalName = "yandex.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "YouCam.exe", OriginalName = "YouCam.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "ZohoAssist.exe", OriginalName = "ZohoAssist.exe", AutoTerminate = false });
			settings.Applications.Blacklist.Add(new BlacklistApplication { ExecutableName = "Zoom.exe", OriginalName = "Zoom.exe", AutoTerminate = false });

			settings.Browser.AdditionalWindow.AllowAddressBar = false;
			settings.Browser.AdditionalWindow.AllowBackwardNavigation = true;
			settings.Browser.AdditionalWindow.AllowDeveloperConsole = false;
			settings.Browser.AdditionalWindow.AllowForwardNavigation = true;
			settings.Browser.AdditionalWindow.AllowReloading = true;
			settings.Browser.AdditionalWindow.FullScreenMode = false;
			settings.Browser.AdditionalWindow.Position = WindowPosition.Right;
			settings.Browser.AdditionalWindow.RelativeHeight = 100;
			settings.Browser.AdditionalWindow.AbsoluteWidth = 1000;
			settings.Browser.AdditionalWindow.ShowHomeButton = false;
			settings.Browser.AdditionalWindow.ShowReloadButton = true;
			settings.Browser.AdditionalWindow.ShowReloadWarning = false;
			settings.Browser.AdditionalWindow.ShowToolbar = true;
			settings.Browser.AdditionalWindow.UrlPolicy = UrlPolicy.Never;
			settings.Browser.AllowAudioCapture = true;
			settings.Browser.AllowConfigurationDownloads = true;
			settings.Browser.AllowCustomDownAndUploadLocation = false;
			settings.Browser.AllowDownloads = true;
			settings.Browser.AllowFind = true;
			settings.Browser.AllowPageZoom = true;
			settings.Browser.AllowPdfReader = true;
			settings.Browser.AllowPdfReaderToolbar = false;
			settings.Browser.AllowPrint = false;
			settings.Browser.AllowScreenCapture = true;
			settings.Browser.AllowUploads = false;
			settings.Browser.AllowVideoCapture = true;
			settings.Browser.DeleteCacheOnShutdown = true;
			settings.Browser.DeleteCookiesOnShutdown = true;
			settings.Browser.DeleteCookiesOnStartup = true;
			settings.Browser.EnableBrowser = true;
			settings.Browser.LogUploadEnabled = true;
			settings.Browser.LogUploadIntervalMs = 10000;
			settings.Browser.MainWindow.AllowAddressBar = false;
			settings.Browser.MainWindow.AllowBackwardNavigation = false;
			settings.Browser.MainWindow.AllowDeveloperConsole = false;
			settings.Browser.MainWindow.AllowForwardNavigation = false;
			settings.Browser.MainWindow.AllowReloading = true;
			settings.Browser.MainWindow.FullScreenMode = false;
			settings.Browser.MainWindow.RelativeHeight = 100;
			settings.Browser.MainWindow.RelativeWidth = 100;
			settings.Browser.MainWindow.ShowHomeButton = false;
			settings.Browser.MainWindow.ShowReloadButton = true;
			settings.Browser.MainWindow.ShowReloadWarning = true;
			settings.Browser.MainWindow.ShowToolbar = true;
			settings.Browser.MainWindow.UrlPolicy = UrlPolicy.Never;
			settings.Browser.PopupPolicy = PopupPolicy.Allow;
			settings.Browser.Proxy.Policy = ProxyPolicy.System;
			settings.Browser.ResetOnQuitUrl = false;
			settings.Browser.SendBrowserExamKey = false;
			settings.Browser.SendConfigurationKey = true;
			settings.Browser.ShowFileSystemElementPath = true;
			settings.Browser.StartUrl = "https://topin-assessment-portal-beta.earlywave.in/seb-acknowledgement";
			settings.Browser.UseCustomUserAgent = false;
			settings.Browser.UseIsolatedClipboard = false;
			settings.Browser.UseQueryParameter = false;
			settings.Browser.UseTemporaryDownAndUploadDirectory = false;
			settings.Browser.LoginToken = null; // Will be populated from URL handler if present

			settings.Audio.InitializeVolume = false;
			settings.Audio.InitialVolume = 25;
			settings.Audio.MuteAudio = false;

			settings.ConfigurationMode = ConfigurationMode.Exam;

			settings.Display.AllowedDisplays = 1;
			settings.Display.AlwaysOn = true;
			settings.Display.IgnoreError = false;
			settings.Display.InternalDisplayOnly = false;

			settings.Keyboard.AllowAltEsc = false;
			settings.Keyboard.AllowAltF4 = false;
			settings.Keyboard.AllowAltTab = false;
			settings.Keyboard.AllowCtrlC = true;
			settings.Keyboard.AllowCtrlEsc = false;
			settings.Keyboard.AllowCtrlV = true;
			settings.Keyboard.AllowCtrlX = true;
			settings.Keyboard.AllowEsc = false;
			settings.Keyboard.AllowF1 = false;
			settings.Keyboard.AllowF2 = false;
			settings.Keyboard.AllowF3 = false;
			settings.Keyboard.AllowF4 = false;
			settings.Keyboard.AllowF5 = false;
			settings.Keyboard.AllowF6 = false;
			settings.Keyboard.AllowF7 = false;
			settings.Keyboard.AllowF8 = false;
			settings.Keyboard.AllowF9 = false;
			settings.Keyboard.AllowF10 = false;
			settings.Keyboard.AllowF11 = false;
			settings.Keyboard.AllowF12 = false;
			settings.Keyboard.AllowPrintScreen = false;
			settings.Keyboard.AllowSystemKey = false;

			settings.LogLevel = LogLevel.Debug;

			settings.Mouse.AllowMiddleButton = false;
			settings.Mouse.AllowRightButton = false;

			settings.PowerSupply.ChargeThresholdCritical = 0.1;
			settings.PowerSupply.ChargeThresholdLow = 0.2;

			settings.Proctoring.Enabled = false;
			settings.Proctoring.ScreenProctoring.CacheSize = 500;
			settings.Proctoring.ScreenProctoring.Enabled = false;
			settings.Proctoring.ScreenProctoring.ImageDownscaling = 1.0;
			settings.Proctoring.ScreenProctoring.ImageFormat = ImageFormat.Png;
			settings.Proctoring.ScreenProctoring.ImageQuantization = ImageQuantization.Grayscale4bpp;
			settings.Proctoring.ScreenProctoring.IntervalMaximum = 5000;
			settings.Proctoring.ScreenProctoring.IntervalMinimum = 1000;
			settings.Proctoring.ScreenProctoring.MetaData.CaptureApplicationData = true;
			settings.Proctoring.ScreenProctoring.MetaData.CaptureBrowserData = true;
			settings.Proctoring.ScreenProctoring.MetaData.CaptureWindowTitle = true;
			settings.Proctoring.ShowTaskbarNotification = true;

			settings.Security.AllowApplicationLogAccess = false;
			settings.Security.AllowReconfiguration = false;
			settings.Security.AllowStickyKeys = false;
			settings.Security.AllowTermination = true;
			settings.Security.AllowWindowCapture = true;
			settings.Security.ClipboardPolicy = ClipboardPolicy.Allow;
			settings.Security.DisableSessionChangeLockScreen = false;
			settings.Security.KioskMode = KioskMode.CreateNewDesktop;
			settings.Security.VerifyCursorConfiguration = true;
			settings.Security.VerifySessionIntegrity = true;
			settings.Security.VirtualMachinePolicy = VirtualMachinePolicy.Deny;

			settings.Server.Invigilation.ForceRaiseHandMessage = false;
			settings.Server.Invigilation.ShowRaiseHandNotification = true;
			settings.Server.PerformFallback = false;
			settings.Server.PingInterval = 1000;
			settings.Server.RequestAttemptInterval = 2000;
			settings.Server.RequestAttempts = 5;
			settings.Server.RequestTimeout = 30000;

			settings.Service.DisableChromeNotifications = true;
			settings.Service.DisableEaseOfAccessOptions = true;
			settings.Service.DisableFindPrinter = true;
			settings.Service.DisableNetworkOptions = true;
			settings.Service.DisablePasswordChange = true;
			settings.Service.DisablePowerOptions = true;
			settings.Service.DisableRemoteConnections = false;
			settings.Service.DisableSignout = true;
			settings.Service.DisableTaskManager = true;
			settings.Service.DisableUserLock = true;
			settings.Service.DisableUserSwitch = true;
			settings.Service.DisableVmwareOverlay = true;
			settings.Service.DisableWindowsUpdate = true;
			settings.Service.IgnoreService = false;
			settings.Service.Policy = ServicePolicy.Mandatory;
			settings.Service.SetVmwareConfiguration = false;

			settings.SessionMode = SessionMode.Normal;

			settings.System.AlwaysOn = true;

			settings.UserInterface.ActionCenter.EnableActionCenter = false;
			settings.UserInterface.ActionCenter.ShowApplicationInfo = true;
			settings.UserInterface.ActionCenter.ShowApplicationLog = false;
			settings.UserInterface.ActionCenter.ShowAudio = true;
			settings.UserInterface.ActionCenter.ShowClock = true;
			settings.UserInterface.ActionCenter.ShowKeyboardLayout = false;
			settings.UserInterface.ActionCenter.ShowNetwork = true;
			settings.UserInterface.LockScreen.BackgroundColor = "#ff0000";
			settings.UserInterface.Mode = UserInterfaceMode.Desktop;
			settings.UserInterface.Taskbar.EnableTaskbar = true;
			settings.UserInterface.Taskbar.ShowApplicationInfo = false;
			settings.UserInterface.Taskbar.ShowApplicationLog = false;
			settings.UserInterface.Taskbar.ShowAudio = true;
			settings.UserInterface.Taskbar.ShowClock = true;
			settings.UserInterface.Taskbar.ShowKeyboardLayout = false;
			settings.UserInterface.Taskbar.ShowNetwork = true;

			return settings;
		}
	}
}
