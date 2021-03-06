﻿// <copyright file="SendFileMiddleware.cs" company="Microsoft Open Technologies, Inc.">
// Copyright 2011-2013 Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Owin.StaticFiles
{
    using AppFunc = Func<IDictionary<string, object>, Task>;
    using SendFileFunc = Func<string, long, long?, CancellationToken, Task>;

    /// <summary>
    /// This middleware provides an efficient fallback mechanism for sending static files
    /// when the server does not natively support such a feature.
    /// The caller is responsible for setting all headers in advance.
    /// The caller is responsible for performing the correct impersonation to give access to the file.
    /// </summary>
    public class SendFileMiddleware
    {
        private readonly AppFunc _next;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        public SendFileMiddleware(AppFunc next)
        {
            _next = next;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="environment"></param>
        /// <returns></returns>
        public Task Invoke(IDictionary<string, object> environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException("environment");
            }

            // Check if there is a SendFile delegate already present
            object obj;
            if (!environment.TryGetValue(Constants.SendFileAsyncKey, out obj) || !(obj is SendFileFunc))
            {
                var output = (Stream)environment[Constants.ResponseBodyKey];
                environment[Constants.SendFileAsyncKey] = new SendFileFunc(new SendFileWrapper(output).SendAsync);
            }

            return _next(environment);
        }

        private class SendFileWrapper
        {
            private readonly Stream _output;

            internal SendFileWrapper(Stream output)
            {
                _output = output;
            }

            // TODO: Detect and throw if the caller tries to start a second operation before the first finishes.
            internal Task SendAsync(string fileName, long offset, long? length, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentNullException("fileName");
                }
                if (!File.Exists(fileName))
                {
                    throw new FileNotFoundException(string.Empty, fileName);
                }

                var fileInfo = new FileInfo(fileName);
                if (offset < 0 || offset > fileInfo.Length)
                {
                    throw new ArgumentOutOfRangeException("offset", offset, string.Empty);
                }

                if (length.HasValue &&
                    (length.Value < 0 || length.Value > fileInfo.Length - offset))
                {
                    throw new ArgumentOutOfRangeException("length", length, string.Empty);
                }

                Stream fileStream = File.OpenRead(fileName);
                try
                {
                    // TODO: Pool buffers between operations.
                    fileStream.Seek(offset, SeekOrigin.Begin);
                    var copyOperation = new StreamCopyOperation(fileStream, _output, length, cancel);
                    return copyOperation.Start()
                        .ContinueWith(resultTask =>
                        {
                            fileStream.Close();
                            resultTask.Wait(); // Throw exceptions, etc.
                        }, TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception)
                {
                    fileStream.Close();
                    throw;
                }
            }
        }
    }
}
