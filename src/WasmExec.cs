// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MSBuildWasm 
{
    public class WasmExec : Microsoft.Build.Utilities.Task, IWasmTask
    {
        public string WasmFilePath { get; set; }
        public string WasmtimeArgs { get; set; }
        public override bool Execute()
        {
            if (!IsWasmtimeInPath())
            {
                Log.LogError("Wasmtime not in path.");
                return false;
            }

            // use Process to just run `wasmtime run <WasmFilePath>`
            StringBuilder command = new StringBuilder();
            command.Append("run ");
            command.Append(WasmFilePath);
            command.Append(" ");
            // TODO: add better task options like mapping directories from xml? etc
            command.Append(WasmtimeArgs);

            ProcessStartInfo psi = new();
            psi.FileName = "wasmtime";
            psi.Arguments = command.ToString();
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (Process process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        //Console.WriteLine(e.Data);
                        Log.LogMessage(MessageImportance.High, e.Data);
                    }
                });

                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    return true;
                }

                return false;
            }
        }

        private static bool IsWasmtimeInPath()
        {
            // check if wasmtime is in path
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "wasmtime";
            psi.Arguments = "--version";
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            Process p = Process.Start(psi);
            p.WaitForExit();
            if (p.ExitCode == 0)
            {
                return true;
            }
            return false;
        }
    }
}
