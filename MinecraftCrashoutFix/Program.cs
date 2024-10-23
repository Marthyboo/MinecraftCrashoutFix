using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Linq;

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

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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

    static Dictionary<string, string> GetRunningUwpApps()
    {
        var uwpApps = new Dictionary<string, string>();
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
        using (var button = new Button())
        {
            form.Text = "Select UWP Application";
            form.ClientSize = new System.Drawing.Size(300, 200);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MaximizeBox = false;

            listBox.Dock = DockStyle.Top;
            listBox.Height = 150;
            foreach (var app in apps)
            {
                listBox.Items.Add($"{app.Key} ({app.Value})");
            }

            button.Text = "Enable Debugging";
            button.Dock = DockStyle.Bottom;
            button.DialogResult = DialogResult.OK;

            form.Controls.Add(listBox);
            form.Controls.Add(button);
            form.AcceptButton = button;

            return form.ShowDialog() == DialogResult.OK && listBox.SelectedItem != null
                ? apps.Values.ElementAt(listBox.SelectedIndex)
                : null;
        }
    }

    static void EnableDebuggingForApp(string packageFamilyName)
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
                    MessageBox.Show($"Debugging enabled for package: {packageFullName}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error enabling debugging for {packageFullName}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Marshal.ReleaseComObject(packageDebugSettings);
                }
            }
            else
            {
                MessageBox.Show($"Error getting package details. Error code: {result}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            MessageBox.Show($"Error searching for package. Error code: {result}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}