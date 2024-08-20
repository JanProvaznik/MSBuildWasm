// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;


namespace MSBuildWasm
{
    /// <summary>
    /// Helper class for isolating files.
    /// </summary>
    internal class FileIsolator
    {
        private readonly TaskLoggingHelper _log;
        private const string InputFileName = "input.json";
        private const string OutputFileName = "output.json";

        internal DirectoryInfo _sharedTmpDir { get; set; }
        internal DirectoryInfo _hostTmpDir { get; set; }
        internal string _inputPath { get; set; }
        internal string _outputPath { get; set; }


        public FileIsolator(TaskLoggingHelper log)
        {
            _log = log;
            CreateTmpDirs();
            _inputPath = Path.Combine(_hostTmpDir.FullName, InputFileName);
            _outputPath = Path.Combine(_hostTmpDir.FullName, OutputFileName);
        }

        /// <summary>
        /// Copies a file or directory from the guest (sandbox) environment to the host.
        /// </summary>
        /// <param name="wasmPath">The path in the sandbox environment.</param>
        /// <param name="itemSpec">The destination path on the host.</param>
        /// <returns>A TaskItem representing the copied item, or null if the item was not found.</returns>

        internal TaskItem CopyGuestToHost(string wasmPath, string itemSpec)
        {
            string sandboxOuterPath = Path.Combine(_sharedTmpDir.FullName, wasmPath);

            if (File.Exists(sandboxOuterPath))
            {
                File.Copy(sandboxOuterPath, itemSpec, overwrite: true);
            }
            else if (Directory.Exists(sandboxOuterPath))
            {
                DirectoryCopy(sandboxOuterPath, itemSpec);
            }
            else
            {
                // nothing to copy
                _log.LogMessage(MessageImportance.Normal, $"Task output not found");
                return null;
            }
            return new TaskItem(itemSpec);

        }
        /// <summary>
        /// Creates directories for sandboxing.
        /// </summary>
        internal void CreateTmpDirs()
        {
            _sharedTmpDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            _hostTmpDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            _log.LogMessage(MessageImportance.Low, $"Created shared temporary directories: {_sharedTmpDir} and {_hostTmpDir}");
        }

        /// <summary>
        /// Removes temporary directories.
        /// </summary>
        internal void Cleanup()
        {
            DeleteTemporaryDirectory(_sharedTmpDir);
            DeleteTemporaryDirectory(_hostTmpDir);
        }

        /// <summary>
        /// Helper function for deleting a directory.
        /// </summary>
        /// <param name="directory"></param>
        private void DeleteTemporaryDirectory(DirectoryInfo directory)
        {
            if (directory != null)
            {
                try
                {
                    Directory.Delete(directory.FullName, true);
                    _log.LogMessage(MessageImportance.Low, $"Removed temporary directory: {directory}");
                }
                catch (Exception ex)
                {
                    _log.LogMessage(MessageImportance.High, $"Failed to remove temporary directory: {directory}. Exception: {ex.Message}");
                }
            }
        }
        private static void DirectoryCopy(string sourcePath, string destinationPath)
        {
            DirectoryInfo diSource = new DirectoryInfo(sourcePath);
            DirectoryInfo diTarget = new DirectoryInfo(destinationPath);

            CopyAll(diSource, diTarget);
        }
        private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir =
                    target.CreateSubdirectory(diSourceSubDir.Name);
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
        /// <summary>
        /// We put all files to the sandbox to the root.
        /// </summary>
        /// <param name="path">original path</param>
        /// <returns>flattened sandbox path</returns>
        private string ConvertToSandboxPath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '_')
                       .Replace(Path.AltDirectorySeparatorChar, '_')
                       .Replace(':', '_');
        }
        /// <summary>
        /// Tries to read the task output from the output file.
        /// </summary>
        /// <param name="taskOutput">output file contents</param>
        /// <returns>true if the output file exists</returns>
        internal bool TryGetTaskOutput(out string taskOutput)
        {
            if (File.Exists(_outputPath))
            {
                taskOutput = File.ReadAllText(_outputPath);
                return true;
            }
            else
            {
                taskOutput = null;
                return false;
            }
        }
        /// <summary>
        /// Copies file/directory to sandbox directory.
        /// </summary>
        /// <param name="taskItem">TaskItem representation of a file/directory</param>
        internal void CopyTaskItemToSandbox(ITaskItem taskItem)
        {
            // ItemSpec = path in usual circumstances
            string sourcePath = taskItem.ItemSpec;
            string sandboxPath = ConvertToSandboxPath(sourcePath);
            string destinationPath = Path.Combine(_sharedTmpDir.FullName, sandboxPath);
            // add metadatum for sandboxPath
            
            taskItem.SetMetadata(Serializer.TaskItemGuestPathPropertyName, sandboxPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            if (Directory.Exists(sourcePath))
            {
                DirectoryCopy(sourcePath, destinationPath);
            }
            else if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath);
            }
            else
            {
                _log.LogMessage(MessageImportance.High, $"Task item {sourcePath} not found.");
            }
            _log.LogMessage(MessageImportance.Low, $"Copied {sourcePath} to {destinationPath}");
        }
    }
}
