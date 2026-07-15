/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using CefSharp;
using SafeExamBrowser.Logging.Contracts;
using BrowserSettings = SafeExamBrowser.Settings.Browser.BrowserSettings;

namespace SafeExamBrowser.Browser.Handlers
{
	internal class PermissionHandler : CefSharp.Handler.PermissionHandler
	{
		private readonly ILogger logger;
		private readonly BrowserSettings settings;

		internal PermissionHandler(ILogger logger, BrowserSettings settings)
		{
			this.logger = logger;
			this.settings = settings;
		}

		protected override bool OnRequestMediaAccessPermission(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string requestingOrigin, MediaAccessPermissionType requestedPermissions, IMediaAccessCallback callback)
		{
			var allowed = MediaAccessPermissionType.None;

			if (settings.AllowAudioCapture)
			{
				allowed |= MediaAccessPermissionType.AudioCapture;
			}

			if (settings.AllowVideoCapture)
			{
				allowed |= MediaAccessPermissionType.VideoCapture;
			}

			if (settings.AllowScreenCapture)
			{
				allowed |= MediaAccessPermissionType.DesktopAudioCapture | MediaAccessPermissionType.DesktopVideoCapture;
			}

			var granted = requestedPermissions & allowed;

			logger.Debug($"Media access requested by '{requestingOrigin}' ({requestedPermissions}) -> granting {granted}.");
			callback.Continue(granted);

			return true;
		}
	}
}
