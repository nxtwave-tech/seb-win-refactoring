/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using CefSharp;
using CefSharp.WinForms;
using CefSharp.WinForms.Host;
using SafeExamBrowser.Browser.Wrapper.Events;

namespace SafeExamBrowser.Browser.Wrapper.Handlers
{
	internal class PermissionHandlerSwitch : CefSharp.Handler.PermissionHandler
	{
		protected override bool OnRequestMediaAccessPermission(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string requestingOrigin, MediaAccessPermissionType requestedPermissions, IMediaAccessCallback callback)
		{
			var args = new GenericEventArgs { Value = false };

			if (browser.IsPopup)
			{
				var control = ChromiumHostControl.FromBrowser(browser) as CefSharpPopupControl;

				control?.OnRequestMediaAccessPermission(chromiumWebBrowser, browser, frame, requestingOrigin, requestedPermissions, callback, args);
			}
			else
			{
				var control = ChromiumWebBrowser.FromBrowser(browser) as CefSharpBrowserControl;

				control?.OnRequestMediaAccessPermission(chromiumWebBrowser, browser, frame, requestingOrigin, requestedPermissions, callback, args);
			}

			return args.Value;
		}
	}
}
