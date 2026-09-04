// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

// SettingsCompatibility.cs intentionally mirrors PowerToys namespace names. Inside that
// namespace, its upstream-style `using Settings.UI.Library.Attributes` is resolved relative
// to the current namespace, so provide this tiny bridge rather than rewriting imported call sites.
namespace Microsoft.PowerToys.Settings.UI.Library.Settings.UI.Library.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class CmdConfigureIgnoreAttribute : Attribute
{
}
