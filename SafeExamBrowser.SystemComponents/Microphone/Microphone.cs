/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Linq;
using System.Timers;
using NAudio.CoreAudioApi;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.SystemComponents.Contracts.Microphone;
using SafeExamBrowser.SystemComponents.Contracts.Microphone.Events;

namespace SafeExamBrowser.SystemComponents.Microphone
{
	public class Microphone : IMicrophone
	{
		private const int PollIntervalMs = 1000;

		private readonly object @lock = new object();
		private readonly ILogger logger;

		private MMDevice inputDevice;
		private string inputDeviceFullName;
		private string inputDeviceShortName;
		private Timer timer;
		private float maxPeakSinceLastLog;
		private int ticksSinceLastLog;

		public string DeviceFullName => inputDeviceFullName ?? string.Empty;
		public string DeviceShortName => inputDeviceShortName ?? string.Empty;
		public bool HasInputDevice => inputDevice != default(MMDevice);

		public event MicrophoneLevelChangedEventHandler LevelChanged;

		public Microphone(ILogger logger)
		{
			this.logger = logger;
		}

		public void Initialize()
		{
			if (TryLoadInputDevice())
			{
				inputDeviceFullName = inputDevice.FriendlyName;
				inputDeviceShortName = inputDevice.FriendlyName.Length > 25 ? inputDevice.FriendlyName.Split(' ').First() : inputDevice.FriendlyName;

				logger.Info($"Found '{inputDevice}' to be the active microphone device. Started monitoring the input level.");

				timer = new Timer(PollIntervalMs) { AutoReset = true };
				timer.Elapsed += Timer_Elapsed;
				timer.Start();
			}
			else
			{
				logger.Warn("Could not find an active microphone (capture) device!");
			}
		}

		public void Terminate()
		{
			lock (@lock)
			{
				if (timer != default(Timer))
				{
					timer.Stop();
					timer.Elapsed -= Timer_Elapsed;
					timer.Dispose();
					timer = default;
				}

				if (inputDevice != default(MMDevice))
				{
					inputDevice.Dispose();
					inputDevice = default;

					logger.Info("Stopped monitoring the microphone device.");
				}
			}
		}

		private bool TryLoadInputDevice()
		{
			using (var enumerator = new MMDeviceEnumerator())
			{
				if (enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
				{
					inputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
				}
				else
				{
					inputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).FirstOrDefault();
				}
			}

			return inputDevice != default(MMDevice);
		}

		private void Timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			lock (@lock)
			{
				if (inputDevice == default(MMDevice))
				{
					return;
				}

				try
				{
					var peak = inputDevice.AudioMeterInformation.MasterPeakValue;

					if (peak > maxPeakSinceLastLog)
					{
						maxPeakSinceLastLog = peak;
					}

					LevelChanged?.Invoke(peak);

					if (++ticksSinceLastLog >= 2)
					{
						logger.Info($"Microphone '{DeviceShortName}' input peak over the last {ticksSinceLastLog}s: {Math.Round(maxPeakSinceLastLog * 100)}%.");
						maxPeakSinceLastLog = 0;
						ticksSinceLastLog = 0;
					}
				}
				catch (Exception ex)
				{
					logger.Error("Failed to read the microphone input level!", ex);
				}
			}
		}
	}
}
