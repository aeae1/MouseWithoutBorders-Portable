using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class EssentialHardeningTests
{
    private string directory = null!;
    private string path = null!;

    [TestInitialize]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "MWB-Hardening", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "preferences.json");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    private static string Document(string key)
    {
        var settings = new MouseWithoutBordersSettings();
        settings.Properties.SecurityKey.Value = key;
        return JsonSerializer.Serialize(settings, SettingsUtils.SerializerOptions);
    }

    [TestMethod]
    public void CorruptPreferencesArePreservedAndRejected()
    {
        File.WriteAllText(path, "broken JSON");
        var utils = new SettingsUtils(path);
        Assert.ThrowsException<InvalidDataException>(() => utils.GetSettingsOrDefault<MouseWithoutBordersSettings>("MouseWithoutBorders"));
        Assert.AreEqual("broken JSON", File.ReadAllText(path));
        Assert.ThrowsException<InvalidDataException>(() => PortableSettingsStore.Write(path, Document("new-key")));
        Assert.AreEqual("broken JSON", File.ReadAllText(path));
    }

    [TestMethod]
    public void InvalidNestedValuesAreRejected()
    {
        foreach (string json in new[] {
            "null", "{\"properties\":null}",
            "{\"properties\":{\"SecurityKey\":null}}",
            "{\"properties\":{\"SecurityKey\":{\"value\":null}}}",
            "{\"properties\":{\"MachineMatrixString\":[null]}}",
            "{\"properties\":{\"TCPPort\":{\"value\":65535}}}",
            "{\"properties\":{\"ShareClipboard\":\"maybe\"}}" })
        {
            Assert.ThrowsException<InvalidDataException>(() => PortableSettingsStore.Parse(json), json);
        }
    }

    [TestMethod]
    public void OlderPreferencesUseDefaultsForMissingFields()
    {
        var settings = PortableSettingsStore.Parse("{\"properties\":{\"WrapMouse\":true}}");
        Assert.IsTrue(settings.Properties.WrapMouse);
        Assert.IsFalse(settings.Properties.KeyboardShortcutsEnabled);
    }

    [TestMethod]
    public void AtomicSaveKeepsPreviousValidDocument()
    {
        PortableSettingsStore.Write(path, Document("old-key"));
        PortableSettingsStore.Write(path, Document("new-key"));
        Assert.AreEqual("new-key", PortableSettingsStore.Read(path).Properties.SecurityKey.Value);
        Assert.AreEqual("old-key", PortableSettingsStore.Read(path + ".bak").Properties.SecurityKey.Value);
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
    }

    [TestMethod]
    public void RecoveryPreservesCorruptOriginalAndBackup()
    {
        File.WriteAllText(path, "broken");
        File.WriteAllText(path + ".bak", Document("backup-key"));
        PortableSettingsStore.RestoreBackup(path);
        Assert.AreEqual("backup-key", PortableSettingsStore.Read(path).Properties.SecurityKey.Value);
        Assert.AreEqual("broken", File.ReadAllText(Directory.GetFiles(directory, "*.corrupt-*").Single()));
        Assert.AreEqual("backup-key", PortableSettingsStore.Read(path + ".bak").Properties.SecurityKey.Value);
    }

    [TestMethod]
    public void InvalidBackupDoesNotReplaceCurrentFile()
    {
        File.WriteAllText(path, "original");
        File.WriteAllText(path + ".bak", "invalid");
        Assert.ThrowsException<InvalidDataException>(() => PortableSettingsStore.RestoreBackup(path));
        Assert.AreEqual("original", File.ReadAllText(path));
    }

    [TestMethod]
    public void ClonedPreferencesDoNotShareMutableProperties()
    {
        var original = new MouseWithoutBordersProperties();
        original.SecurityKey.Value = "old-key";
        original.MachineMatrixString.Add("PC1");
        var clone = (MouseWithoutBordersProperties)original.Clone();
        original.SecurityKey.Value = "new-key";
        original.MachineMatrixString.Add("PC2");
        Assert.AreEqual("old-key", clone.SecurityKey.Value);
        Assert.AreEqual(1, clone.MachineMatrixString.Count);
    }

    [TestMethod]
    public void OwnWriteNotificationDoesNotUndoAnUnsavedEdit()
    {
        PortableSettingsStore.Write(path, Document("same-key"));
        var settings = new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), watchChanges: false);
        settings.SaveSettingsSynchronously();
        settings.DrawMouse = false;
        settings.UpdateSettingsFromJson();
        Assert.IsFalse(settings.DrawMouse);
    }

    [TestMethod]
    public void InvalidReloadRetainsWorkingSettings()
    {
        PortableSettingsStore.Write(path, Document("working-key"));
        var settings = new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), watchChanges: false);
        File.WriteAllText(path, "invalid");
        Assert.ThrowsException<InvalidDataException>(() => settings.UpdateSettingsFromJson());
        Assert.AreEqual("working-key", settings.MyKey);
    }

    [TestMethod]
    public async Task FinalSynchronousSaveWinsOverQueuedWrites()
    {
        PortableSettingsStore.Write(path, Document("start-key"));
        var settings = new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), watchChanges: false);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => settings.MyKey = "key-" + i)));
        settings.SaveKeySynchronously("final-key");
        // Let any previously queued worker run; it must not resurrect an older snapshot.
        await Task.Delay(200);
        Assert.AreEqual("final-key", PortableSettingsStore.Read(path).Properties.SecurityKey.Value);
        Assert.AreEqual("final-key", settings.MyKey);
    }

    [TestMethod]
    public void FailedKeySaveRetainsPreviousKey()
    {
        PortableSettingsStore.Write(path, Document("old-key"));
        var settings = new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), watchChanges: false);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsException<IOException>(() => settings.SaveKeySynchronously("new-key"));
            Assert.AreEqual("old-key", settings.MyKey);
        }
        Assert.AreEqual("old-key", PortableSettingsStore.Read(path).Properties.SecurityKey.Value);
    }

    [TestMethod]
    public void ClipboardComparisonPreservesCapitalizationChanges()
    {
        Assert.IsFalse(Clipboard.IsSameClipboardText("CustomerCode", "CUSTOMERCODE"));
        Assert.IsTrue(Clipboard.IsSameClipboardText("CustomerCode", "CustomerCode"));
    }

    [TestMethod]
    public void RollbackRestoresExistingFilesAndRemovesNewFiles()
    {
        File.WriteAllText(path, "old");
        string added = Path.Combine(directory, "new.exe");
        using (var transaction = new PortableInstallTransaction())
        {
            transaction.TrackFile(path);
            transaction.TrackFile(added);
            File.WriteAllText(path, "replacement");
            File.WriteAllText(added, "new");
        }
        Assert.AreEqual("old", File.ReadAllText(path));
        Assert.IsFalse(File.Exists(added));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.rollback-*").Length);
    }

    [TestMethod]
    public void CompletedInstallKeepsNewFiles()
    {
        File.WriteAllText(path, "old");
        using (var transaction = new PortableInstallTransaction())
        {
            transaction.TrackFile(path);
            File.WriteAllText(path, "new");
            transaction.Complete();
        }
        Assert.AreEqual("new", File.ReadAllText(path));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.rollback-*").Length);
    }

    [TestMethod]
    public void UninstallOnlyRemovesOwnedShortcuts()
    {
        string shortcut = Path.Combine(directory, "MWB.lnk");
        string oldExe = Path.Combine(directory, "old.exe");
        string newExe = Path.Combine(directory, "new.exe");
        File.WriteAllText(oldExe, "old");
        File.WriteAllText(newExe, "new");
        PortableApplication.CreateShortcut(shortcut, newExe);
        PortableApplication.RemoveShortcutIfOwnedBy(shortcut, oldExe);
        Assert.IsTrue(File.Exists(shortcut));
        PortableApplication.RemoveShortcutIfOwnedBy(shortcut, newExe);
        Assert.IsFalse(File.Exists(shortcut));
    }

    [TestMethod]
    public async Task IpcClientAcceptsServerOwnedByCurrentUser()
    {
        string name = "MWB-Test-" + Guid.NewGuid().ToString("N");
        using var server = IpcChannel<object>.CreateServer(name);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        IpcChannel<object>.VerifyServerOwner(client);
    }

    [TestMethod]
    public void IpcPipeAllowsOnlyCurrentUser()
    {
        using var server = IpcChannel<object>.CreateServer("MWB-Test-" + Guid.NewGuid().ToString("N"));
        var security = server.GetAccessControl();
        var current = WindowsIdentity.GetCurrent().User;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>();
        Assert.IsTrue(rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(current)));
        Assert.IsFalse(rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(current)));
    }
}
