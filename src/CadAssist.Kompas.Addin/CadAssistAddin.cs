using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CadAssist.Kompas.Addin
{
    [ComVisible(true)]
    [Guid("E8D97B81-74F6-4D9E-B76C-47D4B687D9F7")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class CadAssistAddin
    {
        private const string AppExe = @"C:\cad-assist-test\app\CadAssist.Kompas.Interop.exe";
        private const string DefaultModelPath = @"C:\cad-assist-test\test.m3d";
        private const string LogPath = @"C:\cad-assist-test\cad-assist-kompas-addin.log";

        public string GetLibraryName()
        {
            return "CAD Assist";
        }

        public short GetProtectNumber()
        {
            return 111;
        }

        public void ExternalRunCommand(short command, short mode, object kompas)
        {
            LaunchTaskWindow();
        }

        public void LaunchTaskWindow()
        {
            try
            {
                WriteLog("Launch requested.");

                if (!File.Exists(AppExe))
                {
                    WriteLog("Executable not found: " + AppExe);
                    return;
                }

                var args = "--model-path \"" + DefaultModelPath + "\" --show-task-window";
                WriteLog("Starting: " + AppExe + " " + args);

                Process.Start(new ProcessStartInfo
                {
                    FileName = AppExe,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(AppExe),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                WriteLog("ERROR: " + ex);
            }
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
            }
            catch
            {
                // KOMPAS library should never fail because of diagnostic logging.
            }
        }

        [ComRegisterFunction]
        public static void RegisterKompasLibrary(Type type)
        {
            var clsid = type.GUID.ToString("B").ToUpperInvariant();
            var clsidKeyPath = @"SOFTWARE\Classes\CLSID\" + clsid;

            using (var clsidKey = Registry.LocalMachine.CreateSubKey(clsidKeyPath))
            {
                if (clsidKey == null)
                {
                    throw new InvalidOperationException("Could not create CLSID registry key: " + clsidKeyPath);
                }

                clsidKey.SetValue(null, "CAD Assist KOMPAS Launcher");
                clsidKey.CreateSubKey("Kompas_Library");

                using (var inproc = clsidKey.CreateSubKey("InprocServer32"))
                {
                    if (inproc != null)
                    {
                        inproc.SetValue(null, Path.Combine(Environment.SystemDirectory, "mscoree.dll"));
                        inproc.SetValue("ThreadingModel", "Both");
                    }
                }
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterKompasLibrary(Type type)
        {
            var clsid = type.GUID.ToString("B").ToUpperInvariant();
            var kompasLibraryKeyPath = @"SOFTWARE\Classes\CLSID\" + clsid + @"\Kompas_Library";

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(kompasLibraryKeyPath, false);
            }
            catch
            {
                // Ignore unregister cleanup errors.
            }
        }
    }
}
