// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

internal static class StandaloneBootstrap
{
    /// <summary>
    /// Enables the built-in tray/settings UI after the portable first-launch flow has
    /// selected a location and created or found its adjacent preferences file.
    /// </summary>
    internal static void InitializeAfterFirstLaunch()
    {
        // PowerToys defaults this to false because its separate Settings application
        // supplies the UI. Standalone MWB must expose its own tray and settings window.
        Setting.Values.ShowOriginalUI = true;
    }
}
