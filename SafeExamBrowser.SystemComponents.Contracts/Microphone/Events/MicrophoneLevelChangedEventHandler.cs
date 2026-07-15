/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

namespace SafeExamBrowser.SystemComponents.Contracts.Microphone.Events
{
	/// <summary>
	/// Indicates that the input level of the microphone system component has changed. The level is a value between 0.0 and 1.0.
	/// </summary>
	public delegate void MicrophoneLevelChangedEventHandler(double level);
}
