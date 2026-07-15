/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CefSharp;
using CefSharp.WinForms;
using SafeExamBrowser.Applications.Contracts.Events;
using SafeExamBrowser.Browser.Contracts;
using SafeExamBrowser.Browser.Contracts.Events;
using SafeExamBrowser.Browser.Events;
using SafeExamBrowser.Browser.Integrations;
using SafeExamBrowser.Configuration.Contracts;
using SafeExamBrowser.Configuration.Contracts.Cryptography;
using SafeExamBrowser.Core.Contracts.Resources.Icons;
using SafeExamBrowser.I18n.Contracts;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.Settings;
using SafeExamBrowser.Settings.Browser.Proxy;
using SafeExamBrowser.Settings.Logging;
using SafeExamBrowser.UserInterface.Contracts;
using SafeExamBrowser.UserInterface.Contracts.FileSystemDialog;
using SafeExamBrowser.UserInterface.Contracts.MessageBox;
using SafeExamBrowser.WindowsApi.Contracts;
using BrowserSettings = SafeExamBrowser.Settings.Browser.BrowserSettings;

namespace SafeExamBrowser.Browser
{
	public class BrowserApplication : IBrowserApplication
	{
		private const string UserAgentTag = "TSB";
		private const string UserAgentOrgIdParameter = "org_id";
		private const string UserAgentCipherKey = "q7Wm3xZ9Kb2Rt6Yn8Lp4Vc5";

		private int windowIdCounter = default;

		private readonly AppConfig appConfig;
		private readonly Clipboard clipboard;
		private readonly IFileSystemDialog fileSystemDialog;
		private readonly IHashAlgorithm hashAlgorithm;
		private readonly IKeyGenerator keyGenerator;
		private readonly IModuleLogger logger;
		private readonly LogUploader logUploader;
		private readonly IMessageBox messageBox;
		private readonly INativeMethods nativeMethods;
		private readonly SessionMode sessionMode;
		private readonly BrowserSettings settings;
		private readonly IText text;
		private readonly IUserInterfaceFactory uiFactory;
		private readonly List<BrowserWindow> windows;

		private System.Timers.Timer logUploadTimer;

		public bool AutoStart { get; private set; }
		public IconResource Icon { get; private set; }
		public Guid Id { get; private set; }
		public string Name { get; private set; }
		public string Tooltip { get; private set; }

		public event DownloadRequestedEventHandler ConfigurationDownloadRequested;
		public event LoseFocusRequestedEventHandler LoseFocusRequested;
		public event TerminationRequestedEventHandler TerminationRequested;
		public event UserIdentifierDetectedEventHandler UserIdentifierDetected;
		public event WindowsChangedEventHandler WindowsChanged;

		public BrowserApplication(
			AppConfig appConfig,
			BrowserSettings settings,
			IFileSystemDialog fileSystemDialog,
			IHashAlgorithm hashAlgorithm,
			IKeyGenerator keyGenerator,
			IMessageBox messageBox,
			IModuleLogger logger,
			INativeMethods nativeMethods,
			SessionMode sessionMode,
			IText text,
			IUserInterfaceFactory uiFactory)
		{
			this.appConfig = appConfig;
			this.clipboard = new Clipboard(logger.CloneFor(nameof(Clipboard)), settings);
			this.logUploader = new LogUploader(appConfig, logger.CloneFor(nameof(LogUploader)), settings);
			this.fileSystemDialog = fileSystemDialog;
			this.hashAlgorithm = hashAlgorithm;
			this.keyGenerator = keyGenerator;
			this.logger = logger;
			this.messageBox = messageBox;
			this.nativeMethods = nativeMethods;
			this.sessionMode = sessionMode;
			this.settings = settings;
			this.text = text;
			this.uiFactory = uiFactory;
			this.windows = new List<BrowserWindow>();

			this.logUploader.CredentialsRequired += LogUploader_CredentialsRequired;
			this.logUploader.DebugMessage += LogUploader_DebugMessage;
		}

		public void Focus(bool forward)
		{
			windows.ForEach(window =>
			{
				window.Focus(forward);
			});
		}

		public IEnumerable<IBrowserWindow> GetWindows()
		{
			return new List<IBrowserWindow>(windows);
		}

		public void Initialize()
		{
			logger.Info("Starting initialization...");

			var cefSettings = InitializeCefSettings();
			var success = Cef.Initialize(cefSettings, true, default(IApp));

			InitializeApplicationInfo();

			if (success)
			{
				InitializeCookies();
				InitializeDownAndUploadDirectory();
				InitializeIntegrityKeys();
				InitializePreferences();

				logger.Info("Initialized browser.");
			}
			else
			{
				throw new Exception($"Failed to initialize browser (exit code: {Cef.GetExitCode()})!");
			}
		}

		public void Start()
		{
			CreateNewWindow();
			StartLogUpload();
		}

		public void Terminate()
		{
			logger.Info("Initiating termination...");

			StopLogUpload();

			AwaitReady();

			foreach (var window in windows)
			{
				window.Closed -= Window_Closed;
				window.Close();

				logger.Info($"Closed browser window #{window.Id}.");
			}

			FinalizeCookies();
			FinalizeDownAndUploadDirectory();
			Cef.Shutdown();
			FinalizeCache();

			logger.Info("Terminated browser.");
		}

		private void AwaitReady()
		{
			// We apparently need to let the browser finish any pending work before attempting to reset or terminate it, especially if the
			// reset or termination is initiated automatically (e.g. by a quit URL). Otherwise, the engine will crash on some occasions, seemingly
			// when it can't finish handling its events (like ChromiumWebBrowser.LoadError).

			Thread.Sleep(500);
		}

		private void StartLogUpload()
		{
			if (!settings.LogUploadEnabled)
			{
				logger.Info("Log upload is disabled.");
				return;
			}

			var interval = settings.LogUploadIntervalMs > 0 ? settings.LogUploadIntervalMs : 10000;

			logUploadTimer = new System.Timers.Timer(interval) { AutoReset = true };
			logUploadTimer.Elapsed += (o, e) => logUploader.Tick();
			logUploadTimer.Start();

			logger.Info($"Started log upload timer with an interval of {interval} ms.");
		}

		private void StopLogUpload()
		{
			if (logUploadTimer != default)
			{
				logUploadTimer.Stop();
				logUploadTimer.Dispose();
				logUploadTimer = default;
			}

			if (settings.LogUploadEnabled)
			{
				logger.Info("Performing final log upload before termination...");
				logUploader.Flush();
			}
		}

		private void LogUploader_CredentialsRequired()
		{
			windows.FirstOrDefault(w => w.IsMainWindow)?.Control.ExecuteJavaScript("TopinSecureBrowser.log._requestCredentials();");
		}

		private void LogUploader_DebugMessage(string message)
		{
			var encoded = Newtonsoft.Json.JsonConvert.SerializeObject(message);

			windows.FirstOrDefault(w => w.IsMainWindow)?.Control.ExecuteJavaScript($"TopinSecureBrowser.log._debug({encoded});");
		}

		private void CreateNewWindow(PopupRequestedEventArgs args = default)
		{
			var id = ++windowIdCounter;
			var integrations = new Integration[]
			{
				new GenericIntegration(logger.CloneFor($"{nameof(GenericIntegration)} #{id}")),
				new EdxIntegration(logger.CloneFor($"{nameof(EdxIntegration)} #{id}")),
				new MoodleIntegration(logger.CloneFor($"{nameof(MoodleIntegration)} #{id}"))
			};
			var isMainWindow = windows.Count == 0;
			var startUrl = GenerateStartUrl();
			var windowLogger = logger.CloneFor($"Browser Window #{id}");
			var window = new BrowserWindow(
				appConfig,
				clipboard,
				fileSystemDialog,
				hashAlgorithm,
				id,
				integrations,
				isMainWindow,
				keyGenerator,
				windowLogger,
				logUploader,
				messageBox,
				sessionMode,
				settings,
				startUrl,
				text,
				uiFactory);

			window.Closed += Window_Closed;
			window.ConfigurationDownloadRequested += (f, a) => ConfigurationDownloadRequested?.Invoke(f, a);
			window.PopupRequested += Window_PopupRequested;
			window.ResetRequested += Window_ResetRequested;
			window.UserIdentifierDetected += (i) => UserIdentifierDetected?.Invoke(i);
			window.TerminationRequested += () => TerminationRequested?.Invoke();
			window.LoseFocusRequested += (forward) => LoseFocusRequested?.Invoke(forward);

			window.InitializeControl();
			windows.Add(window);

			if (args != default)
			{
				args.Window = window;
			}
			else
			{
				window.InitializeWindow();
			}

			logger.Info($"Created browser window #{window.Id}.");
			WindowsChanged?.Invoke();
		}

		private void DeleteCookies()
		{
			var callback = new TaskDeleteCookiesCallback();
			var cookieManager = Cef.GetGlobalCookieManager();

			callback.Task.ContinueWith(task =>
			{
				if (!task.IsCompleted || task.Result == TaskDeleteCookiesCallback.InvalidNoOfCookiesDeleted)
				{
					logger.Warn("Failed to delete cookies!");
				}
				else
				{
					logger.Debug($"Deleted {task.Result} cookies.");
				}
			});

			if (cookieManager != default && cookieManager.DeleteCookies(callback: callback))
			{
				logger.Debug("Successfully initiated cookie deletion.");
			}
			else
			{
				logger.Warn("Failed to initiate cookie deletion!");
			}
		}

		private void FinalizeCache()
		{
			if (settings.DeleteCacheOnShutdown && settings.DeleteCookiesOnShutdown)
			{
				try
				{
					Directory.Delete(appConfig.BrowserCachePath, true);
					logger.Info("Deleted browser cache.");
				}
				catch (Exception e)
				{
					logger.Error("Failed to delete browser cache!", e);
				}
			}
			else
			{
				logger.Info("Retained browser cache.");
			}
		}

		private void FinalizeCookies()
		{
			if (settings.DeleteCookiesOnShutdown)
			{
				DeleteCookies();
			}
		}

		private void FinalizeDownAndUploadDirectory()
		{
			if (settings.UseTemporaryDownAndUploadDirectory)
			{
				try
				{
					Directory.Delete(settings.DownAndUploadDirectory, true);
					logger.Info("Deleted temporary down- and upload directory.");
				}
				catch (Exception e)
				{
					logger.Error("Failed to delete temporary down- and upload directory!", e);
				}
			}
		}

	private string GenerateStartUrl()
	{
		var url = settings.StartUrl;

		if (settings.UseQueryParameter)
		{
			if (url.Contains("?") && settings.StartUrlQuery?.Length > 1 && Uri.TryCreate(url, UriKind.Absolute, out var uri))
			{
				url = url.Replace(uri.Query, $"{uri.Query}&{settings.StartUrlQuery.Substring(1)}");
			}
			else
			{
				url = $"{url}{settings.StartUrlQuery}";
			}
		}

		// Inject login token if available
		if (!string.IsNullOrEmpty(settings.LoginToken))
		{
			var tokenParam = $"token={Uri.EscapeDataString(settings.LoginToken)}";
			
			if (url.Contains("?"))
			{
				url = $"{url}&{tokenParam}";
			}
			else
			{
				url = $"{url}?{tokenParam}";
			}
			
			logger.Info("Login token injected into start URL.");
		}

		return url;
	}

		private void InitializeApplicationInfo()
		{
			AutoStart = true;
			Icon = new BrowserIconResource();
			Id = Guid.NewGuid();
			Name = text.Get(TextKey.Browser_Name);
			Tooltip = text.Get(TextKey.Browser_Tooltip);
		}

		private CefSettings InitializeCefSettings()
		{
			var warning = logger.LogLevel == LogLevel.Warning;
			var error = logger.LogLevel == LogLevel.Error;
			var cefSettings = new CefSettings();

			cefSettings.AcceptLanguageList = CultureInfo.CurrentUICulture.Name;
			cefSettings.CachePath = appConfig.BrowserCachePath;
			cefSettings.LogFile = appConfig.BrowserLogFilePath;
			cefSettings.LogSeverity = error ? LogSeverity.Error : (warning ? LogSeverity.Warning : LogSeverity.Info);
			cefSettings.PersistSessionCookies = !settings.DeleteCookiesOnStartup || !settings.DeleteCookiesOnShutdown;
			cefSettings.UserAgent = InitializeUserAgent();

			if (!settings.AllowPageZoom)
			{
				cefSettings.CefCommandLineArgs.Add("disable-pinch");
			}

			if (!settings.AllowPdfReader)
			{
				cefSettings.CefCommandLineArgs.Add("disable-pdf-extension");
			}

			if (!settings.AllowSpellChecking)
			{
				cefSettings.CefCommandLineArgs.Add("disable-spell-checking");
			}

			cefSettings.CefCommandLineArgs.Add("enable-media-stream");
			cefSettings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing");
			cefSettings.CefCommandLineArgs.Add("use-fake-ui-for-media-stream");
			cefSettings.CefCommandLineArgs.Add("disable-features", "WebRtcHideLocalIpsWithMdns,AudioServiceOutOfProcess");
			cefSettings.CefCommandLineArgs.Add("touch-events", "enabled");

			InitializeProxySettings(cefSettings);

			logger.Debug($"Accept Language: {cefSettings.AcceptLanguageList}");
			logger.Debug($"Cache Path: {cefSettings.CachePath}");
			logger.Debug($"Engine Version: Chromium {Cef.ChromiumVersion}, CEF {Cef.CefVersion}, CefSharp {Cef.CefSharpVersion}");
			logger.Debug($"Log File: {cefSettings.LogFile}");
			logger.Debug($"Log Severity: {cefSettings.LogSeverity}.");
			logger.Debug($"PDF Reader: {(settings.AllowPdfReader ? "Enabled" : "Disabled")}.");
			logger.Debug($"Session Persistence: {(cefSettings.PersistSessionCookies ? "Enabled" : "Disabled")}.");

			return cefSettings;
		}

		private void InitializeCookies()
		{
			if (settings.DeleteCookiesOnStartup)
			{
				DeleteCookies();
			}
		}

		private void InitializeDownAndUploadDirectory()
		{
			if (settings.UseTemporaryDownAndUploadDirectory)
			{
				InitializeTemporaryDownAndUploadDirectory();
			}
			else if (!string.IsNullOrEmpty(settings.DownAndUploadDirectory))
			{
				InitializeCustomDownAndUploadDirectory();
			}
		}

		private void InitializeCustomDownAndUploadDirectory()
		{
			if (!Directory.Exists(Environment.ExpandEnvironmentVariables(settings.DownAndUploadDirectory)))
			{
				logger.Warn("The configured down- and upload directory does not exist! Falling back to the default directory...");
				settings.DownAndUploadDirectory = default;
			}
			else
			{
				logger.Debug("Using custom down- and upload directory as defined in the active configuration.");
			}
		}

		private void InitializeTemporaryDownAndUploadDirectory()
		{
			try
			{
				settings.DownAndUploadDirectory = Path.Combine(appConfig.TemporaryDirectory, Path.GetRandomFileName());
				Directory.CreateDirectory(settings.DownAndUploadDirectory);
				logger.Info($"Created temporary down- and upload directory.");
			}
			catch (Exception e)
			{
				logger.Error("Failed to create temporary down- and upload directory!", e);
			}
		}

		private void InitializeIntegrityKeys()
		{
			logger.Debug($"Browser Exam Key (BEK) transmission is {(settings.SendBrowserExamKey ? "enabled" : "disabled")}.");
			logger.Debug($"Configuration Key (CK) transmission is {(settings.SendConfigurationKey ? "enabled" : "disabled")}.");

			if (settings.CustomBrowserExamKey != default)
			{
				keyGenerator.UseCustomBrowserExamKey(settings.CustomBrowserExamKey);
				logger.Debug($"The browser application will be using a custom browser exam key.");
			}
			else
			{
				logger.Debug($"The browser application will be using the default browser exam key.");
			}
		}

		private void InitializePreferences()
		{
			Cef.UIThreadTaskFactory.StartNew(() =>
			{
				using (var requestContext = Cef.GetGlobalRequestContext())
				{
					requestContext.SetPreference("autofill.credit_card_enabled", false, out _);
					requestContext.SetPreference("autofill.profile_enabled", false, out _);
				}
			});
		}

		private void InitializeProxySettings(CefSettings cefSettings)
		{
			if (settings.Proxy.Policy == ProxyPolicy.Custom)
			{
				if (settings.Proxy.AutoConfigure)
				{
					cefSettings.CefCommandLineArgs.Add("proxy-pac-url", settings.Proxy.AutoConfigureUrl);
				}

				if (settings.Proxy.AutoDetect)
				{
					cefSettings.CefCommandLineArgs.Add("proxy-auto-detect", "");
				}

				if (settings.Proxy.BypassList.Any())
				{
					cefSettings.CefCommandLineArgs.Add("proxy-bypass-list", string.Join(";", settings.Proxy.BypassList));
				}

				if (settings.Proxy.Proxies.Any())
				{
					var proxies = new List<string>();

					foreach (var proxy in settings.Proxy.Proxies)
					{
						proxies.Add($"{ToScheme(proxy.Protocol)}={proxy.Host}:{proxy.Port}");
					}

					cefSettings.CefCommandLineArgs.Add("proxy-server", string.Join(";", proxies));
				}
			}
		}

		private string InitializeUserAgent()
		{
			var osVersion = $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
			var userAgent = default(string);

			if (settings.UseCustomUserAgent)
			{
				userAgent = settings.CustomUserAgent;
			}
			else
			{
				userAgent = $"Mozilla/5.0 (Windows NT {osVersion}; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{Cef.ChromiumVersion} Safari/537.36";
			}

			userAgent = $"{userAgent} {BuildUserAgentTag()}";

			if (!string.IsNullOrWhiteSpace(settings.UserAgentSuffix))
			{
				userAgent = $"{userAgent} {settings.UserAgentSuffix}";
			}

			return userAgent;
		}

		private string BuildUserAgentTag()
		{
			var orgId = ExtractOrgId(settings.StartUrl);

			if (string.IsNullOrWhiteSpace(orgId))
			{
				logger.Info("No organization identifier found in the start URL, using the default user agent tag.");

				return UserAgentTag;
			}

			var token = CipherOrgId(orgId);

			logger.Info("Appended the organization-specific verification token to the user agent.");

			return $"{UserAgentTag} {token}";
		}

		private string ExtractOrgId(string startUrl)
		{
			if (string.IsNullOrWhiteSpace(startUrl))
			{
				return default;
			}

			try
			{
				var query = new Uri(startUrl).Query.TrimStart('?');

				foreach (var pair in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
				{
					var parts = pair.Split(new[] { '=' }, 2);

					if (parts.Length == 2 && parts[0].Equals(UserAgentOrgIdParameter, StringComparison.OrdinalIgnoreCase))
					{
						return Uri.UnescapeDataString(parts[1]);
					}
				}
			}
			catch (Exception e)
			{
				logger.Error("Failed to extract the organization identifier from the start URL!", e);
			}

			return default;
		}

		private string CipherOrgId(string orgId)
		{
			var data = Encoding.UTF8.GetBytes(orgId);
			var key = Encoding.UTF8.GetBytes(UserAgentCipherKey);

			for (var i = 0; i < data.Length; i++)
			{
				data[i] = (byte) (data[i] ^ key[i % key.Length]);
			}

			return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		private string ToScheme(ProxyProtocol protocol)
		{
			switch (protocol)
			{
				case ProxyProtocol.Ftp:
					return Uri.UriSchemeFtp;
				case ProxyProtocol.Http:
					return Uri.UriSchemeHttp;
				case ProxyProtocol.Https:
					return Uri.UriSchemeHttps;
				case ProxyProtocol.Socks:
					return "socks";
			}

			throw new NotImplementedException($"Mapping for proxy protocol '{protocol}' is not yet implemented!");
		}

		private void Window_Closed(int id)
		{
			windows.Remove(windows.First(i => i.Id == id));
			WindowsChanged?.Invoke();
			logger.Info($"Window #{id} has been closed.");
		}

		private void Window_PopupRequested(PopupRequestedEventArgs args)
		{
			logger.Info($"Received request to create new window...");
			CreateNewWindow(args);
		}

		private void Window_ResetRequested()
		{
			logger.Info("Attempting to reset browser...");

			AwaitReady();

			foreach (var window in windows)
			{
				window.Closed -= Window_Closed;
				window.Close();
				logger.Info($"Closed browser window #{window.Id}.");
			}

			windows.Clear();
			WindowsChanged?.Invoke();

			if (settings.DeleteCookiesOnStartup && settings.DeleteCookiesOnShutdown)
			{
				DeleteCookies();
			}

			nativeMethods.EmptyClipboard();
			CreateNewWindow();
			logger.Info("Successfully reset browser.");
		}
	}
}
