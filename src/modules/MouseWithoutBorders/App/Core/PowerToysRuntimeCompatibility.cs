// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ManagedCommon
{
    /// <summary>
    /// Standalone replacement for the PowerToys ETW lifetime object.
    /// The standalone fork does not emit PowerToys ETW telemetry.
    /// </summary>
    internal sealed class ETWTrace : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// PowerToys modules normally quit when the PowerToys runner exits. A standalone app
    /// has no runner to watch, so registering this callback intentionally does nothing.
    /// </summary>
    internal static class RunnerHelper
    {
        public static void WaitForPowerToysRunnerExitFallback(Action powerToysRunnerExitedCallback)
        {
            _ = powerToysRunnerExitedCallback;
        }
    }

    /// <summary>
    /// Small file logger matching the subset of ManagedCommon.Logger used by MWB.
    /// </summary>
    internal static class Logger
    {
        private static readonly object FileLock = new();
        private static string logFilePath;

        public static void InitializeLogger(string relativePath)
        {
            _ = relativePath;

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MouseWithoutBorders",
                "Logs");

            Directory.CreateDirectory(directory);
            logFilePath = Path.Combine(directory, "MouseWithoutBorders.log");
        }

        public static void LogInfo(
            string message,
            string memberName = "",
            string sourceFilePath = "",
            int sourceLineNumber = 0)
        {
            try
            {
                var builder = new StringBuilder();
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.Append(" [");
                builder.Append(Environment.ProcessId);
                builder.Append(':');
                builder.Append(Thread.CurrentThread.ManagedThreadId);
                builder.Append("] ");
                builder.Append(message);

                if (!string.IsNullOrWhiteSpace(memberName))
                {
                    builder.Append(" (");
                    builder.Append(memberName);
                    if (!string.IsNullOrWhiteSpace(sourceFilePath))
                    {
                        builder.Append(" @ ");
                        builder.Append(Path.GetFileName(sourceFilePath));
                        if (sourceLineNumber > 0)
                        {
                            builder.Append(':');
                            builder.Append(sourceLineNumber);
                        }
                    }

                    builder.Append(')');
                }

                var line = builder.ToString();
                Debug.WriteLine(line);

                if (string.IsNullOrEmpty(logFilePath))
                {
                    InitializeLogger(string.Empty);
                }

                lock (FileLock)
                {
                    File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never be able to take down input sharing.
            }
        }
    }
}

namespace Microsoft.PowerToys.Telemetry
{
    /// <summary>
    /// Compatibility value used only by the imported event classes. No telemetry is sent.
    /// </summary>
    public enum PartA_PrivTags
    {
        ProductAndServiceUsage = 0,
    }

    /// <summary>
    /// No-op replacement for PowerToys telemetry. Keeping the API shape lets the MWB
    /// call sites stay close to upstream while ensuring the standalone fork emits none.
    /// </summary>
    internal static class PowerToysTelemetry
    {
        public static NoOpTelemetryLogger Log { get; } = new();

        internal sealed class NoOpTelemetryLogger
        {
            public void WriteEvent<T>(T telemetryEvent)
            {
                _ = telemetryEvent;
            }
        }
    }
}

namespace Microsoft.PowerToys.Telemetry.Events
{
    public class EventBase
    {
    }

    public interface IEvent
    {
    }
}
