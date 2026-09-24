// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Principal;
using Microsoft.Win32;

namespace NightBrightness;

internal static class StartupRegistration
{
    const string TaskName = "NightBrightness";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        if (enabled)
        {
            // Keep the Run entry as a fallback if Task Scheduler is unavailable.
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(TaskName, "\"" + Environment.ProcessPath + "\" --background");
            try { RegisterTask(); }
            catch (Exception e) { Program.Log("Fast sign-in task unavailable; using Windows startup entry: " + e); }
        }
        else
        {
            RemoveTask();
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(TaskName, false);
        }
    }

    static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not start.");
        service.Connect();
        return service;
    }

    static void RegisterTask()
    {
        dynamic service = Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic task = service.NewTask(0);
        string userId = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not identify the signed-in Windows user.");

        task.RegistrationInfo.Description = "Apply Night Brightness immediately when this user signs in.";
        task.Principal.UserId = userId;
        task.Principal.LogonType = 3; // InteractiveToken: desktop and tray access.
        task.Principal.RunLevel = 0; // Least privilege; no UAC prompt.
        task.Settings.Enabled = true;
        task.Settings.Priority = 4; // Higher priority than Task Scheduler's default 7.
        task.Settings.StartWhenAvailable = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.RestartCount = 3;
        task.Settings.RestartInterval = "PT1M";

        dynamic trigger = task.Triggers.Create(9); // TASK_TRIGGER_LOGON; no delay.
        trigger.UserId = userId;
        dynamic action = task.Actions.Create(0); // TASK_ACTION_EXEC.
        action.Path = Environment.ProcessPath;
        action.Arguments = "--background";
        folder.RegisterTaskDefinition(TaskName, task, 6, null, null, 3, null);
    }

    static void RemoveTask()
    {
        dynamic folder = Connect().GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); }
        catch (System.Runtime.InteropServices.COMException e) when ((uint)e.HResult == 0x80070002) { }
    }
}
