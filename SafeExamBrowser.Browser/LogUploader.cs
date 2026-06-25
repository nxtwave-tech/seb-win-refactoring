/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CefSharp;
using Newtonsoft.Json;
using SafeExamBrowser.Configuration.Contracts;
using SafeExamBrowser.Logging.Contracts;
using BrowserSettings = SafeExamBrowser.Settings.Browser.BrowserSettings;

namespace SafeExamBrowser.Browser
{
	/// <summary>
	/// Uploads the on-disk session log files directly to S3 using short-lived, PUT-scoped credentials provided by the
	/// exam website. Credential fields are never written to the application log (the logs themselves are uploaded).
	/// </summary>
	internal class LogUploader
	{
		private const int EXPIRY_BUFFER_MINUTES = 2;

		private readonly AppConfig appConfig;
		private readonly ILogger logger;
		private readonly object @lock = new object();
		private readonly BrowserSettings settings;

		private UploadConfig config;
		private bool isUploading;
		private string launchId;

		/// <summary>
		/// Raised when the current credentials are missing, expired (or about to expire) or were rejected by S3. The
		/// consumer is expected to ask the website for fresh credentials.
		/// </summary>
		internal event Action CredentialsRequired;

		/// <summary>
		/// TEMPORARY DEBUG: raised with a human-readable message for each meaningful upload step or error, so the
		/// consumer can surface it on-page (dev tools are blocked in TSB/SEB). Never carries credential material.
		/// </summary>
		internal event Action<string> DebugMessage;

		internal LogUploader(AppConfig appConfig, ILogger logger, BrowserSettings settings)
		{
			this.appConfig = appConfig;
			this.logger = logger;
			this.settings = settings;
		}

		/// <summary>
		/// Stores the session context and credentials provided by the website. Malformed messages are ignored.
		/// </summary>
		internal void Configure(JavascriptMessageReceivedEventArgs message)
		{
			try
			{
				var parsed = message.ConvertMessageTo<ConfigMessage>();
				var payload = parsed?.Payload;

				Debug("Received a LogUploadConfig message from the website.");

				if (payload == null || payload.Credentials == null)
				{
					logger.Warn("Received a log upload configuration without a valid payload, ignoring it.");
					Debug("Ignored config: missing payload or credentials.");
					return;
				}

				if (string.IsNullOrWhiteSpace(payload.Environment) ||
					string.IsNullOrWhiteSpace(payload.OrgAssessmentId) ||
					string.IsNullOrWhiteSpace(payload.UserId) ||
					string.IsNullOrWhiteSpace(payload.Region) ||
					string.IsNullOrWhiteSpace(payload.Bucket))
				{
					logger.Warn("Received an incomplete log upload configuration, ignoring it.");
					Debug("Ignored config: incomplete fields (environment/orgAssessmentId/userId/region/bucket).");
					return;
				}

				if (string.IsNullOrWhiteSpace(payload.Credentials.AccessKeyId) ||
					string.IsNullOrWhiteSpace(payload.Credentials.SecretAccessKey) ||
					string.IsNullOrWhiteSpace(payload.Credentials.SessionToken))
				{
					logger.Warn("Received a log upload configuration without valid credentials, ignoring it.");
					Debug("Ignored config: missing one or more credential fields.");
					return;
				}

				lock (@lock)
				{
					config = new UploadConfig
					{
						Environment = payload.Environment.Trim(),
						OrgAssessmentId = payload.OrgAssessmentId.Trim(),
						UserId = payload.UserId.Trim(),
						AttemptId = payload.AttemptId?.Trim(),
						Region = payload.Region.Trim(),
						Bucket = payload.Bucket.Trim(),
						AccessKeyId = payload.Credentials.AccessKeyId,
						SecretAccessKey = payload.Credentials.SecretAccessKey,
						SessionToken = payload.Credentials.SessionToken,
						Expiration = payload.Credentials.Expiration
					};
				}

				logger.Info("Received and stored a new log upload configuration.");
				Debug($"Stored config: env={config.Environment}, bucket={config.Bucket}, region={config.Region}, orgAssessmentId={config.OrgAssessmentId}, userId={config.UserId}.");
			}
			catch (Exception e)
			{
				logger.Error("Failed to process the log upload configuration message.", e);
				Debug($"Failed to process config message: {e.Message}");
			}
		}

		/// <summary>
		/// Periodic upload cycle, invoked by the upload timer.
		/// </summary>
		internal void Tick()
		{
			Upload();
		}

		/// <summary>
		/// Final upload cycle, invoked on shutdown to capture the latest log content.
		/// </summary>
		internal void Flush()
		{
			Upload();
		}

		private void Upload()
		{
			UploadConfig current;

			lock (@lock)
			{
				if (!settings.LogUploadEnabled || config == null || isUploading)
				{
					Debug($"Upload skipped: enabled={settings.LogUploadEnabled}, hasConfig={config != null}, alreadyUploading={isUploading}.");
					return;
				}

				isUploading = true;
				current = config;
			}

			try
			{
				if (IsExpired(current))
				{
					logger.Info("The log upload credentials are expired or about to expire, requesting fresh ones.");
					Debug("Credentials expired or about to expire, requesting fresh ones.");
					CredentialsRequired?.Invoke();
					return;
				}

				using (var client = CreateClient(current))
				{
					var prefix = BuildPrefix(current);

					Debug($"Starting upload to bucket '{current.Bucket}' with prefix '{prefix}'.");

					UploadManifest(client, current, prefix);
					Debug("Uploaded manifest.json.");

					foreach (var file in GetLogFiles())
					{
						UploadFile(client, current.Bucket, $"{prefix}{file.Key}", file.Value);
					}

					Debug("Upload cycle complete.");
				}
			}
			catch (AmazonS3Exception e) when (IsCredentialError(e))
			{
				logger.Warn("A log upload was rejected due to a credential issue, requesting fresh credentials.");
				Debug($"Upload rejected (credential issue): code={e.ErrorCode}, status={e.StatusCode}. Requesting fresh credentials.");
				CredentialsRequired?.Invoke();
			}
			catch (AmazonS3Exception e)
			{
				logger.Error("Failed to upload session logs to S3.", e);
				Debug($"S3 error: code={e.ErrorCode}, status={e.StatusCode}, message={e.Message}");
			}
			catch (Exception e)
			{
				logger.Error("Failed to upload session logs to S3.", e);
				Debug($"Upload failed: {e.GetType().Name} - {e.Message}");
			}
			finally
			{
				lock (@lock)
				{
					isUploading = false;
				}
			}
		}

		private void UploadFile(IAmazonS3 client, string bucket, string key, string filePath)
		{
			if (!File.Exists(filePath))
			{
				Debug($"Skipped '{key}': file not found on disk.");
				return;
			}

			byte[] content;

			using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var buffer = new MemoryStream())
			{
				stream.CopyTo(buffer);
				content = buffer.ToArray();
			}

			var request = new PutObjectRequest
			{
				BucketName = bucket,
				Key = key,
				InputStream = new MemoryStream(content),
				ContentType = "text/plain"
			};

			client.PutObjectAsync(request).GetAwaiter().GetResult();

			Debug($"Uploaded '{key}' ({content.Length} bytes).");
		}

		private void UploadManifest(IAmazonS3 client, UploadConfig current, string prefix)
		{
			var manifest = new
			{
				launchId,
				environment = current.Environment,
				orgAssessmentId = current.OrgAssessmentId,
				userId = current.UserId,
				attemptId = current.AttemptId,
				appVersion = appConfig.ProgramInformationalVersion,
				buildVersion = appConfig.ProgramBuildVersion,
				osVersion = Environment.OSVersion.VersionString,
				machineName = Environment.MachineName,
				startTime = appConfig.ApplicationStartTime.ToUniversalTime().ToString("o"),
				files = new[] { "Runtime.log", "Client.log", "Browser.log", "Service.log" }
			};

			var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
			var bytes = System.Text.Encoding.UTF8.GetBytes(json);

			var request = new PutObjectRequest
			{
				BucketName = current.Bucket,
				Key = $"{prefix}manifest.json",
				InputStream = new MemoryStream(bytes),
				ContentType = "application/json"
			};

			client.PutObjectAsync(request).GetAwaiter().GetResult();
		}

		private IAmazonS3 CreateClient(UploadConfig current)
		{
			var credentials = new SessionAWSCredentials(current.AccessKeyId, current.SecretAccessKey, current.SessionToken);
			var region = RegionEndpoint.GetBySystemName(current.Region);

			return new AmazonS3Client(credentials, region);
		}

		private string BuildPrefix(UploadConfig current)
		{
			if (launchId == null)
			{
				var timestamp = appConfig.ApplicationStartTime.ToString("yyyy-MM-dd\\_HH\\hmm\\mss\\s");
				var shortId = appConfig.ClientId.ToString("N").Substring(0, 8);

				launchId = $"{timestamp}_{shortId}";
			}

			return $"topin_{current.Environment}/tsb_logs/{current.OrgAssessmentId}/{current.UserId}/{launchId}/";
		}

		private IEnumerable<KeyValuePair<string, string>> GetLogFiles()
		{
			yield return new KeyValuePair<string, string>("Runtime.log", appConfig.RuntimeLogFilePath);
			yield return new KeyValuePair<string, string>("Client.log", appConfig.ClientLogFilePath);
			yield return new KeyValuePair<string, string>("Browser.log", appConfig.BrowserLogFilePath);
			yield return new KeyValuePair<string, string>("Service.log", appConfig.ServiceLogFilePath);
		}

		private bool IsExpired(UploadConfig current)
		{
			if (!current.Expiration.HasValue)
			{
				return false;
			}

			return current.Expiration.Value.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(EXPIRY_BUFFER_MINUTES);
		}

		private void Debug(string message)
		{
			DebugMessage?.Invoke(message);
		}

		private bool IsCredentialError(AmazonS3Exception e)
		{
			return e.StatusCode == HttpStatusCode.Forbidden ||
				string.Equals(e.ErrorCode, "ExpiredToken", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(e.ErrorCode, "InvalidToken", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(e.ErrorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(e.ErrorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase);
		}

		private class ConfigMessage
		{
			public string Type { get; set; }
			public ConfigPayload Payload { get; set; }
		}

		private class ConfigPayload
		{
			public string Environment { get; set; }
			public string OrgAssessmentId { get; set; }
			public string UserId { get; set; }
			public string AttemptId { get; set; }
			public string Region { get; set; }
			public string Bucket { get; set; }
			public CredentialsData Credentials { get; set; }
		}

		private class CredentialsData
		{
			public string AccessKeyId { get; set; }
			public string SecretAccessKey { get; set; }
			public string SessionToken { get; set; }
			public DateTime? Expiration { get; set; }
		}

		private class UploadConfig
		{
			public string Environment { get; set; }
			public string OrgAssessmentId { get; set; }
			public string UserId { get; set; }
			public string AttemptId { get; set; }
			public string Region { get; set; }
			public string Bucket { get; set; }
			public string AccessKeyId { get; set; }
			public string SecretAccessKey { get; set; }
			public string SessionToken { get; set; }
			public DateTime? Expiration { get; set; }
		}
	}
}
