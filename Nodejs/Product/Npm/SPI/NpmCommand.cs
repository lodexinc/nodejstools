﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudioTools.Project;
using Microsoft.Win32;

namespace Microsoft.NodejsTools.Npm.SPI {
    internal abstract class NpmCommand : AbstractNpmLogSource {
        private readonly string _fullPathToRootPackageDirectory;
        private string _pathToNpm;
        private bool _useFallbackIfNpmNotFound;
        private readonly ManualResetEvent _cancellation;
        private StringBuilder _output = new StringBuilder();
        private StringBuilder _error = new StringBuilder();
        private object _bufferLock = new object();

        protected NpmCommand(
            string fullPathToRootPackageDirectory,
            string pathToNpm = null,
            bool useFallbackIfNpmNotFound = true
        ) {
            _fullPathToRootPackageDirectory = fullPathToRootPackageDirectory;
            _pathToNpm = pathToNpm;
            _useFallbackIfNpmNotFound = useFallbackIfNpmNotFound;
            _cancellation = new ManualResetEvent(false);
        }

        protected string Arguments { get; set; }

        protected string GetPathToNpm() {
            if (null == _pathToNpm || !File.Exists(_pathToNpm)) {
                if (_useFallbackIfNpmNotFound) {
                    string match = null;

                    using (var key = Registry.CurrentUser.OpenSubKey("Software\\Node.js")) {
                        if (key != null) {
                            match = key.GetValue("InstallPath") as string;
                            if (Directory.Exists(match)) {
                                match = Path.Combine(match, "npm.cmd");
                                if (!File.Exists(match)) {
                                    match = null;
                                }
                            }
                        }
                    }

                    if (null == match) {
                        foreach (var potential in Environment.GetEnvironmentVariable("path").Split(Path.PathSeparator)) {
                            var path = Path.Combine(potential, "npm.cmd");
                            if (File.Exists(path)) {
                                if (null == match ||
                                    path.Contains(
                                        string.Format(
                                            "{0}nodejs{1}",
                                            Path.DirectorySeparatorChar,
                                            Path.DirectorySeparatorChar))) {
                                    match = path;
                                }
                            }
                        }
                    }

                    if (null != match) {
                        _pathToNpm = match;
                    }
                }

                if (null == _pathToNpm || !File.Exists(_pathToNpm)) {
                    throw new NpmNotFoundException(
                        string.Format(
                            "Cannot find npm.cmd at '{0}' nor on your system PATH. Ensure node.js is installed.",
                            _pathToNpm ?? "(null)"
                        )
                    );
                }
            }
            return _pathToNpm;
        }

        public string StandardOutput {
            get {
                lock (_bufferLock) {
                    return _output.ToString();
                }
            }
        }

        public string StandardError {
            get {
                lock (_bufferLock) {
                    return _error.ToString();
                }
            }
        }

        public void CancelCurrentTask() {
            _cancellation.Set();
        }

        public virtual async Task<bool> ExecuteAsync() {
            OnCommandStarted();
            var wasCancelled = false;
            var redirector = new NpmCommandRedirector(this);
            redirector.WriteLine(string.Format("====Executing command 'npm {0}'====\r\n\r\n", Arguments));
            using (var process = ProcessOutput.Run(
                GetPathToNpm(),
                new [] { Arguments },
                _fullPathToRootPackageDirectory,
                null,
                false,
                redirector,
                quoteArgs: false
            )) {
                var whnd = process.WaitHandle;
                if (whnd == null) {
                    // Process failed to start, and any exception message has
                    // already been sent through the redirectory
                    redirector.WriteErrorLine("Error executing npm - unable to start the npm process");
                } else {
                    var handles = new [] { _cancellation, whnd };
                    var i = await Task.Run(() => WaitHandle.WaitAny(handles));
                    if (i == 0) {
                        wasCancelled = true;
                        process.Kill();
                        redirector.WriteErrorLine("\r\n====npm command cancelled====\r\n\r\n");
                        _cancellation.Reset();
                    } else {
                        Debug.Assert(process.ExitCode.HasValue, "npm process has not really exited");
                        process.Wait();
                        redirector.WriteLine(string.Format(
                            "\r\n====npm command completed with exit code {0}====\r\n\r\n",
                            process.ExitCode ?? -1
                        ));
                    }
                }
            }
            OnCommandCompleted(Arguments, redirector.HasErrors, wasCancelled);
            return !redirector.HasErrors;
        }

        private class NpmCommandRedirector : Redirector {
            NpmCommand _owner;
            
            public NpmCommandRedirector(NpmCommand owner) {
                _owner = owner;
            }

            public bool HasErrors { get; private set; }

            private string AppendToBuffer(StringBuilder buffer, string data) {
                if (data != null) {
                    lock (_owner._bufferLock) {
                        data = Encoding.UTF8.GetString(Console.OutputEncoding.GetBytes(data)) + Environment.NewLine;
                        buffer.Append(data);
                    }
                }
                return data;
            }

            public override void WriteLine(string line) {
                _owner.OnOutputLogged(AppendToBuffer(_owner._output, line));
            }

            public override void WriteErrorLine(string line) {
                HasErrors = true;
                _owner.OnErrorLogged(AppendToBuffer(_owner._error, line));
            }
        }
    }
}