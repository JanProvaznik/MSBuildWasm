// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MSBuildWasm
{

    public interface BuildEngine11 : Microsoft.Build.Framework.IBuildEngine10 { }
    internal class WasmTaskHost : BuildEngine11
    {
        public WasmTaskHost() { }

        public EngineServices EngineServices => throw new NotImplementedException();

        public bool AllowFailureWithoutError { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool IsRunningMultipleNodes => throw new NotImplementedException();

        public bool ContinueOnError => throw new NotImplementedException();

        public int LineNumberOfTaskNode => throw new NotImplementedException();

        public int ColumnNumberOfTaskNode => throw new NotImplementedException();

        public string ProjectFileOfTaskNode => throw new NotImplementedException();

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs, string toolsVersion) => throw new NotImplementedException();
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotImplementedException();
        public BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IList<string>[] removeGlobalProperties, string[] toolsVersion, bool returnTargetOutputs) => throw new NotImplementedException();
        public bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IDictionary[] targetOutputsPerProject, string[] toolsVersion, bool useResultsCache, bool unloadProjectsOnCompletion) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> GetGlobalProperties() => throw new NotImplementedException();
        public object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotImplementedException();
        public void LogCustomEvent(CustomBuildEventArgs e) => throw new NotImplementedException();
        public void LogErrorEvent(BuildErrorEventArgs e) => throw new NotImplementedException();
        public void LogMessageEvent(BuildMessageEventArgs e) => throw new NotImplementedException();
        public void LogTelemetry(string eventName, IDictionary<string, string> properties) => throw new NotImplementedException();
        public void LogWarningEvent(BuildWarningEventArgs e) => throw new NotImplementedException();
        public void Reacquire() => throw new NotImplementedException();
        public void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection) => throw new NotImplementedException();
        public void ReleaseCores(int coresToRelease) => throw new NotImplementedException();
        public int RequestCores(int requestedCores) => throw new NotImplementedException();
        public bool ShouldTreatWarningAsError(string warningCode) => throw new NotImplementedException();
        public object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotImplementedException();
        public void Yield() => throw new NotImplementedException();
    }
}
