/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.SystemComponents.Contracts.Microphone.Events;

namespace SafeExamBrowser.SystemComponents.Contracts.Microphone
{
	/// <summary>
	/// Defines the functionality of the microphone system component.
	/// </summary>
	public interface IMicrophone : ISystemComponent
	{
		/// <summary>
		/// The full name of the microphone (capture) device, or an empty string if not available.
		/// </summary>
		string DeviceFullName { get; }

		/// <summary>
		/// The short microphone (capture) device name, or an empty string if not available.
		/// </summary>
		string DeviceShortName { get; }

		/// <summary>
		/// Indicates whether an active microphone (capture) device is available.
		/// </summary>
		bool HasInputDevice { get; }

		/// <summary>
		/// Fired when the input level of the microphone has changed.
		/// </summary>
		event MicrophoneLevelChangedEventHandler LevelChanged;
	}
}
