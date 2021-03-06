// <copyright file="StaticFileContext.cs" company="Microsoft Open Technologies, Inc.">
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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles.Infrastructure;

namespace Microsoft.Owin.StaticFiles
{
    using SendFileFunc = Func<string, long, long?, CancellationToken, Task>;

    internal struct StaticFileContext
    {
        private readonly IDictionary<string, object> _environment;
        private readonly StaticFileOptions _options;
        private readonly string _matchUrl;
        private readonly IOwinRequest _request;
        private readonly IOwinResponse _response;
        private string _method;
        private bool _isGet;
        private bool _isHead;
        private string _subPath;
        private string _contentType;
        private IFileInfo _fileInfo;
        private long _length;
        private DateTime _lastModified;
        private string _lastModifiedString;
        private string _etag;

        private PreconditionState _ifMatchState;
        private PreconditionState _ifNoneMatchState;
        private PreconditionState _ifModifiedSinceState;
        private PreconditionState _ifUnmodifiedSinceState;

        public StaticFileContext(IDictionary<string, object> environment, StaticFileOptions options, string matchUrl)
        {
            _environment = environment;
            _options = options;
            _matchUrl = matchUrl;
            _request = new OwinRequest(environment);
            _response = new OwinResponse(environment);

            _method = null;
            _isGet = false;
            _isHead = false;
            _subPath = null;
            _contentType = null;
            _fileInfo = null;
            _length = 0;
            _lastModified = new DateTime();
            _etag = null;
            _lastModifiedString = null;
            _ifMatchState = PreconditionState.Unspecified;
            _ifNoneMatchState = PreconditionState.Unspecified;
            _ifModifiedSinceState = PreconditionState.Unspecified;
            _ifUnmodifiedSinceState = PreconditionState.Unspecified;
        }

        internal enum PreconditionState
        {
            Unspecified,
            NotModified,
            ShouldProcess,
            PreconditionFailed
        }

        public bool IsHeadMethod
        {
            get { return _isHead; }
        }

        public bool ValidateMethod()
        {
            _method = _request.Method;
            _isGet = string.Equals(_method, "GET", StringComparison.OrdinalIgnoreCase);
            _isHead = string.Equals(_method, "HEAD", StringComparison.OrdinalIgnoreCase);
            return _isGet || _isHead;
        }

        public bool ValidatePath()
        {
            return Helpers.TryMatchPath(_environment, _matchUrl, forDirectory: false, subpath: out _subPath);
        }

        public bool LookupContentType()
        {
            if (_options.ContentTypeProvider.TryGetContentType(_subPath, out _contentType))
            {
                return true;
            }

            if (_options.ServeUnknownFileTypes)
            {
                _contentType = _options.DefaultContentType;
                return true;
            }

            return false;
        }

        public bool LookupFileInfo()
        {
            bool found = _options.FileSystem.TryGetFileInfo(_subPath, out _fileInfo);
            if (found)
            {
                _length = _fileInfo.Length;
                _lastModified = _fileInfo.LastModified;
                _lastModifiedString = _lastModified.ToString("r", CultureInfo.InvariantCulture);

                long etagHash = _lastModified.ToFileTimeUtc() ^ _length;
                _etag = '\"' + Convert.ToString(etagHash, 16) + '\"';
            }
            return found;
        }

        public void ComprehendRequestHeaders()
        {
            string etag = _etag;

            IList<string> ifMatch = _request.Headers.GetValues("If-Match");
            if (ifMatch != null)
            {
                _ifMatchState = PreconditionState.PreconditionFailed;
                foreach (var segment in new HeaderSegmentCollection(ifMatch))
                {
                    if (segment.Data.Equals(etag, StringComparison.Ordinal))
                    {
                        _ifMatchState = PreconditionState.ShouldProcess;
                        break;
                    }
                }
            }

            IList<string> ifNoneMatch = _request.Headers.GetValues("If-None-Match");
            if (ifNoneMatch != null)
            {
                _ifNoneMatchState = PreconditionState.ShouldProcess;
                foreach (var segment in new HeaderSegmentCollection(ifNoneMatch))
                {
                    if (segment.Data.Equals(etag, StringComparison.Ordinal))
                    {
                        _ifNoneMatchState = PreconditionState.NotModified;
                        break;
                    }
                }
            }

            string ifModifiedSince = _request.Headers.Get("If-Modified-Since");
            if (ifModifiedSince != null)
            {
                bool matches = string.Equals(ifModifiedSince, _lastModifiedString, StringComparison.Ordinal);
                _ifModifiedSinceState = matches ? PreconditionState.NotModified : PreconditionState.ShouldProcess;
            }

            string ifUnmodifiedSince = _request.Headers.Get("If-Unmodified-Since");
            if (ifUnmodifiedSince != null)
            {
                bool matches = string.Equals(ifModifiedSince, _lastModifiedString, StringComparison.Ordinal);
                _ifUnmodifiedSinceState = matches ? PreconditionState.ShouldProcess : PreconditionState.PreconditionFailed;
            }
        }

        public void ApplyResponseHeaders()
        {
            if (!string.IsNullOrEmpty(_contentType))
            {
                _response.ContentType = _contentType;
            }

            _response.Headers.Set("Last-Modified", _lastModifiedString);
            _response.ETag = _etag;
        }

        public PreconditionState GetPreconditionState()
        {
            PreconditionState matchState = _ifMatchState > _ifNoneMatchState ? _ifMatchState : _ifNoneMatchState;
            PreconditionState modifiedState = _ifModifiedSinceState > _ifUnmodifiedSinceState ? _ifModifiedSinceState : _ifUnmodifiedSinceState;
            return matchState > modifiedState ? matchState : modifiedState;
        }

        public Task SendStatusAsync(int statusCode)
        {
            _response.StatusCode = statusCode;
            if (statusCode == Constants.Status200Ok)
            {
                _response.ContentLength = _length;
            }
            return Constants.CompletedTask;
        }

        public Task SendAsync(int statusCode)
        {
            _response.StatusCode = statusCode;
            _response.ContentLength = _length;

            string physicalPath = _fileInfo.PhysicalPath;
            SendFileFunc sendFile = _response.Get<SendFileFunc>("sendfile.SendAsync");
            if (sendFile != null && !string.IsNullOrEmpty(physicalPath))
            {
                return sendFile(physicalPath, 0, _length, _request.CallCancelled);
            }

            Stream readStream = _fileInfo.CreateReadStream();
            var copyOperation = new StreamCopyOperation(readStream, _response.Body, _length, _request.CallCancelled);
            Task task = copyOperation.Start();
            task.ContinueWith(resultTask => readStream.Close(), TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }
    }
}
