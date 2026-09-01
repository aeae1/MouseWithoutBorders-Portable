// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

internal static class StandaloneBootstrap
{
    /// <summary>
    /// PowerToys defaults ShowOriginalUI to false because its separate Settings application
    /// supplies the configuration UI. Standalone MWB has no PowerToys Settings process, so
    /// force the self-contained MWB tray/settings UI on before normal startup.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Setting.Values.ShowOriginalUI = true;
    }
}
