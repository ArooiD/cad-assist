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
            ShowTasks();
        }

        public void ShowTasks()
        {
            var appExe = @"C:\cad-assist-test\app\CadAssist.Kompas.Interop.exe";

            if (!File.Exists(appExe))
            {
                throw new FileNotFoundException("CAD Assist executable was not found", appExe);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = appExe,
                Arguments = "--model-path C:\\cad-assist-test\\test.m3d --show-task-window",
                UseShellExecute = true
            });
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

                clsidKey.SetValue(null, "CAD Assist KOMPAS Library");
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
