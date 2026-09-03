// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

using ManagedCommon;
using Microsoft.PowerToys.Telemetry;

#if PORTABLE_SINGLE_FILE
using MouseWithoutBorders;
#endif

#if PORTABLE_SINGLE_FILE
namespace MouseWithoutBorders.HelperHost
#else
namespace MouseWithoutBorders
#endif
{
    internal static class Program
    {
#if STANDALONE
        private const string AppExecutableName = "MouseWithoutBorders.exe";
#else
        private const string AppExecutableName = "PowerToys.MouseWithoutBorders.exe";
#endif

        internal static FormHelper FormHelper;

        private static FormDot dotForm;

        internal static FormDot DotForm
        {
            get
            {
                return dotForm != null && !dotForm.IsDisposed ? dotForm : (dotForm = new FormDot());
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
#if PORTABLE_SINGLE_FILE
        internal static void Run()
#else
        private static void Main()
#endif
        {
            if (PowerToys.GPOWrapper.GPOWrapper.GetConfiguredMouseWithoutBordersEnabledValue() == PowerToys.GPOWrapper.GpoRuleConfigured.Disabled)
            {
                // TODO: Add logging.
                // Logger.LogWarning("Tried to start with a GPO policy setting the utility to always be disabled. Please contact your systems administrator.");
                return;
            }

            ETWTrace etwTrace = new ETWTrace();

            RunnerHelper.WaitForPowerToysRunnerExitFallback(() =>
            {
                etwTrace?.Dispose();
                Application.Exit();
            });

            string[] args = Environment.GetCommandLineArgs();

            int commandIndex = 1;
#if PORTABLE_SINGLE_FILE
            if (args.Length > 1 && args[1].Equals(Core.PortableApplication.ClipboardHelperArgument, StringComparison.OrdinalIgnoreCase))
            {
                commandIndex = 2;
            }
#endif

            if (args.Length > commandIndex && !string.IsNullOrEmpty(args[commandIndex]))
            {
                string command = args[commandIndex];
                string arg = args.Length > commandIndex + 1 && !string.IsNullOrEmpty(args[commandIndex + 1]) ? args[commandIndex + 1] : string.Empty;

                if (command.Equals("SvcExec", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), AppExecutableName), "\"" + arg + "\"");
                }
                else if (command.Equals("install", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), AppExecutableName));
                }
                else if (command.Equals("help-ex", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(@"http://www.aka.ms/mm");
                }
                else if (command.Equals("InternalError", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(arg, Application.ProductName);
                }

                return;
            }

            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetCompatibleTextRenderingDefault(false);

            dotForm = new FormDot();
            Application.Run(FormHelper = new FormHelper());

            etwTrace?.Dispose();
        }
    }
}
