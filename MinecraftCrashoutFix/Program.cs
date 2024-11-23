using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using System.IO;
using System.Xml.Serialization;
using IWshRuntimeLibrary;

class Program
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, IntPtr[] packageFullNames, ref uint bufferLength, char[] buffer);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int GetPackageFamilyName(IntPtr hProcess, ref uint packageFamilyNameLength, StringBuilder packageFamilyName);

    [ComImport, Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")]
    class PackageDebugSettings { }

    [ComImport, Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPackageDebugSettings
    {
        void EnableDebugging(string packageFullName, string debuggerCommandLine, IntPtr environment);
        void DisableDebugging(string packageFullName);
        void Suspend(string packageFullName);
        void Resume(string packageFullName);
        void TerminateAllProcesses(string packageFullName);
        void SetTargetSessionId(int sessionId);
        void EnumerateBackgroundTasks(string packageFullName, out uint taskCount, out IntPtr taskIds, out IntPtr taskNames);
        void ActivateBackgroundTask(IntPtr taskId);
        void StartServicing(string packageFullName);
        void StopServicing(string packageFullName);
        void StartSessionRedirection(string packageFullName, uint sessionId);
        void StopSessionRedirection(string packageFullName);
        void GetPackageExecutionState(string packageFullName, out uint executionState);
        void RegisterForPackageStateChanges(string packageFullName, IntPtr pPackageExecutionStateChangeNotification, out uint token);
        void UnregisterForPackageStateChanges(uint token);
    }

    private const string StorageFile = "uwp_apps.xml";
    private static List<UwpAppInfo> storedApps;

    [STAThread]
    static void Main(string[] args)
    {
        bool silentMode = args.Length > 0 && args[0] == "--silent";

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        LoadStoredApps();

        // Automatically re-enable debugging for stored apps
        foreach (var app in storedApps)
        {
            EnableDebuggingForApp(app.PackageFamilyName);
        }

        if (!silentMode)
        {
            var runningUwpApps = GetRunningUwpApps();

            if (runningUwpApps.Count == 0)
            {
                MessageBox.Show("No running UWP applications starting with 'M' found.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedApp = ShowAppSelectionDialog(runningUwpApps);

            if (string.IsNullOrEmpty(selectedApp))
            {
                MessageBox.Show("No application selected.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            EnableDebuggingForApp(selectedApp);
        }
    }

    static Dictionary<string, string> GetRunningUwpApps()
    {
        var uwpApps = new Dictionary<string, string>();

        // Add stored apps to the dictionary
        foreach (var app in storedApps)
        {
            uwpApps[app.ProcessName] = app.PackageFamilyName;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.StartsWith("M", StringComparison.OrdinalIgnoreCase))
                {
                    uint bufferLength = 0;
                    if (GetPackageFamilyName(process.Handle, ref bufferLength, null) == 122) // ERROR_INSUFFICIENT_BUFFER
                    {
                        var sb = new StringBuilder((int)bufferLength);
                        if (GetPackageFamilyName(process.Handle, ref bufferLength, sb) == 0) // ERROR_SUCCESS
                        {
                            string packageFamilyName = sb.ToString();
                            if (!string.IsNullOrEmpty(packageFamilyName))
                            {
                                uwpApps[process.ProcessName] = packageFamilyName;

                                // Store the app information if it's not already stored
                                if (!storedApps.Exists(a => a.ProcessName == process.ProcessName && a.PackageFamilyName == packageFamilyName))
                                {
                                    storedApps.Add(new UwpAppInfo { ProcessName = process.ProcessName, PackageFamilyName = packageFamilyName });
                                    SaveStoredApps();
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore any errors and continue with the next process
            }
            finally
            {
                process.Dispose();
            }
        }
        return uwpApps;
    }

    static string ShowAppSelectionDialog(Dictionary<string, string> apps)
    {
        using (var form = new Form())
        using (var listBox = new ListBox())
        using (var buttonEnable = new Button())
        using (var buttonStartup = new Button())
        {
            form.Text = "Select UWP Application";
            form.ClientSize = new System.Drawing.Size(300, 250);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MaximizeBox = false;

            listBox.Dock = DockStyle.Top;
            listBox.Height = 150;
            foreach (var app in apps)
            {
                listBox.Items.Add($"{app.Key} ({app.Value})");
            }

            buttonEnable.Text = "Enable Debugging";
            buttonEnable.Dock = DockStyle.Bottom;
            buttonEnable.DialogResult = DialogResult.OK;

            buttonStartup.Text = "Create Startup Task";
            buttonStartup.Dock = DockStyle.Bottom;
            buttonStartup.Click += (sender, e) => CreateStartupTask();

            form.Controls.Add(listBox);
            form.Controls.Add(buttonEnable);
            form.Controls.Add(buttonStartup);
            form.AcceptButton = buttonEnable;

            return form.ShowDialog() == DialogResult.OK && listBox.SelectedItem != null
                ? apps.Values.ElementAt(listBox.SelectedIndex)
                : null;
        }
    }

    static bool EnableDebuggingForApp(string packageFamilyName)
    {
        uint count = 0;
        uint bufferLength = 0;

        int result = GetPackagesByPackageFamily(packageFamilyName, ref count, null, ref bufferLength, null);
        if (result == 122) // ERROR_INSUFFICIENT_BUFFER
        {
            IntPtr[] packageFullNames = new IntPtr[count];
            char[] buffer = new char[bufferLength];

            result = GetPackagesByPackageFamily(packageFamilyName, ref count, packageFullNames, ref bufferLength, buffer);
            if (result == 0 && count > 0) // ERROR_SUCCESS
            {
                string packageFullName = Marshal.PtrToStringUni(packageFullNames[0]);
                IPackageDebugSettings packageDebugSettings = (IPackageDebugSettings)new PackageDebugSettings();

                try
                {
                    packageDebugSettings.EnableDebugging(packageFullName, null, IntPtr.Zero);
                    Console.WriteLine($"Debugging enabled for package: {packageFullName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error enabling debugging for {packageFullName}: {ex.Message}");
                    return false;
                }
                finally
                {
                    Marshal.ReleaseComObject(packageDebugSettings);
                }
            }
            else
            {
                Console.WriteLine($"Error getting package details. Error code: {result}");
                return false;
            }
        }
        else
        {
            Console.WriteLine($"Error searching for package. Error code: {result}");
            return false;
        }
    }

    private static void LoadStoredApps()
    {
        if (System.IO.File.Exists(StorageFile))
        {
            var serializer = new XmlSerializer(typeof(List<UwpAppInfo>));
            using (var reader = new System.IO.StreamReader(StorageFile))
            {
                storedApps = (List<UwpAppInfo>)serializer.Deserialize(reader);
            }
        }
        else
        {
            storedApps = new List<UwpAppInfo>();
        }
    }

    private static void SaveStoredApps()
    {
        var serializer = new XmlSerializer(typeof(List<UwpAppInfo>));
        using (var writer = new System.IO.StreamWriter(StorageFile))
        {
            serializer.Serialize(writer, storedApps);
        }
    }
// The createstartuptask runs when booting up your computer, making you not have to redo the steps you previously did.
    static void CreateStartupTask()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        string shortcutPath = System.IO.Path.Combine(startupFolder, "MinecraftCrashoutFix.lnk");

        if (!System.IO.File.Exists(shortcutPath))
        {
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Application.ExecutablePath;
            shortcut.Arguments = "--silent";
            shortcut.WorkingDirectory = Application.StartupPath;
            shortcut.Description = "MinecraftCrashoutFix Startup";
            shortcut.Save();

            MessageBox.Show("Startup task created. The program will run automatically on system startup.", "Startup Task Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

public class UwpAppInfo
{
    public string ProcessName { get; set; }
    public string PackageFamilyName { get; set; }
}
