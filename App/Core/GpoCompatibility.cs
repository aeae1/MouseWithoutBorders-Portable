// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Security;

using Microsoft.Win32;

// Keep the existing namespace/API surface so the imported Mouse Without Borders code can
// remain close to upstream while the standalone build drops the native PowerToys GPOWrapper.
namespace PowerToys.GPOWrapper;

internal enum GpoRuleConfigured
{
    WrongValue = -3,
    Unavailable = -2,
    NotConfigured = -1,
    Disabled = 0,
    Enabled = 1,
}

internal static class GPOWrapper
{
    private const string PoliciesPath = @"SOFTWARE\Policies\PowerToys";

    private const string GlobalUtilityEnabled = "ConfigureGlobalUtilityEnabledState";
    private const string MouseWithoutBordersEnabled = "ConfigureEnabledUtilityMouseWithoutBorders";
    private const string ClipboardSharingEnabled = "MwbClipboardSharingEnabled";
    private const string FileTransferEnabled = "MwbFileTransferEnabled";
    private const string UseOriginalUserInterface = "MwbUseOriginalUserInterface";
    private const string DisallowBlockingScreensaver = "MwbDisallowBlockingScreensaver";
    private const string AllowServiceMode = "MwbAllowServiceMode";
    private const string SameSubnetOnly = "MwbSameSubnetOnly";
    private const string ValidateRemoteIp = "MwbValidateRemoteIp";
    private const string DisableUserDefinedIpMappingRules = "MwbDisableUserDefinedIpMappingRules";
    private const string PolicyDefinedIpMappingRules = "MwbPolicyDefinedIpMappingRules";

    internal static GpoRuleConfigured GetConfiguredMouseWithoutBordersEnabledValue()
    {
        var individualValue = GetConfiguredValue(MouseWithoutBordersEnabled);
        return individualValue is GpoRuleConfigured.Enabled or GpoRuleConfigured.Disabled
            ? individualValue
            : GetConfiguredValue(GlobalUtilityEnabled);
    }

    internal static GpoRuleConfigured GetConfiguredMwbClipboardSharingEnabledValue() => GetConfiguredValue(ClipboardSharingEnabled);

    internal static GpoRuleConfigured GetConfiguredMwbFileTransferEnabledValue() => GetConfiguredValue(FileTransferEnabled);

    internal static GpoRuleConfigured GetConfiguredMwbUseOriginalUserInterfaceValue() => GetConfiguredValue(UseOriginalUserInterface);

    internal static GpoRuleConfigured GetConfiguredMwbDisallowBlockingScreensaverValue() => GetConfiguredValue(DisallowBlockingScreensaver);

    internal static GpoRuleConfigured GetConfiguredMwbAllowServiceModeValue() => GetConfiguredValue(AllowServiceMode);

    internal static GpoRuleConfigured GetConfiguredMwbSameSubnetOnlyValue() => GetConfiguredValue(SameSubnetOnly);

    internal static GpoRuleConfigured GetConfiguredMwbValidateRemoteIpValue() => GetConfiguredValue(ValidateRemoteIp);

    internal static GpoRuleConfigured GetConfiguredMwbDisableUserDefinedIpMappingRulesValue() => GetConfiguredValue(DisableUserDefinedIpMappingRules);

    internal static string GetConfiguredMwbPolicyDefinedIpMappingRules()
    {
        var machineResult = TryReadRawValue(Registry.LocalMachine, PolicyDefinedIpMappingRules, out var value);
        if (machineResult == RegistryReadResult.Found)
        {
            return ConvertPolicyString(value);
        }

        var userResult = TryReadRawValue(Registry.CurrentUser, PolicyDefinedIpMappingRules, out value);
        return userResult == RegistryReadResult.Found ? ConvertPolicyString(value) : string.Empty;
    }

    private static GpoRuleConfigured GetConfiguredValue(string valueName)
    {
        var machineResult = TryReadRawValue(Registry.LocalMachine, valueName, out var value);
        if (machineResult == RegistryReadResult.Found)
        {
            return ConvertPolicyValue(value);
        }

        var userResult = TryReadRawValue(Registry.CurrentUser, valueName, out value);
        if (userResult == RegistryReadResult.Found)
        {
            return ConvertPolicyValue(value);
        }

        return userResult == RegistryReadResult.Unavailable
            ? GpoRuleConfigured.Unavailable
            : GpoRuleConfigured.NotConfigured;
    }

    private static RegistryReadResult TryReadRawValue(RegistryKey root, string valueName, out object value)
    {
        value = null;

        try
        {
            using var policyKey = root.OpenSubKey(PoliciesPath, writable: false);
            if (policyKey == null)
            {
                return RegistryReadResult.NotFound;
            }

            value = policyKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value == null ? RegistryReadResult.NotFound : RegistryReadResult.Found;
        }
        catch (UnauthorizedAccessException)
        {
            return RegistryReadResult.Unavailable;
        }
        catch (SecurityException)
        {
            return RegistryReadResult.Unavailable;
        }
        catch (IOException)
        {
            return RegistryReadResult.Unavailable;
        }
    }

    private static GpoRuleConfigured ConvertPolicyValue(object value)
    {
        var numericValue = value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            _ => int.MinValue,
        };

        return numericValue switch
        {
            0 => GpoRuleConfigured.Disabled,
            1 => GpoRuleConfigured.Enabled,
            _ => GpoRuleConfigured.WrongValue,
        };
    }

    private static string ConvertPolicyString(object value)
    {
        return value switch
        {
            string text => text,
            string[] lines => string.Join("\r\n", lines),
            _ => string.Empty,
        };
    }

    private enum RegistryReadResult
    {
        NotFound,
        Found,
        Unavailable,
    }
}
