using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CadAssist.Kompas.Addin;

[ComVisible(true)]
[Guid("E8D97B81-74F6-4D9E-B76C-47D4B687D9F7")]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class CadAssistAddin
{
    public string Name => "CAD Assist";

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
}
