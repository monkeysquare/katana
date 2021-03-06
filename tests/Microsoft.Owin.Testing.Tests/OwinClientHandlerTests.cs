﻿// <copyright file="WorkItem22.cs" company="Microsoft Open Technologies, Inc.">
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

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Owin.Testing.Tests
{
    public class OwinClientHandlerTests
    {
        [Fact]
        public void LeadingQuestionMarkInQueryIsRemoved()
        {
            /* http://katanaproject.codeplex.com/workitem/22
             * 
             * Summary
             * 
             * The owin spec for the "owin.RequestQueryString" key: 
             *    
             *    A string containing the query string component of the HTTP request URI,
             *    without the leading “?” (e.g., "foo=bar&baz=quux"). The value may be an
             *    empty string.
             *    
             *  request.RequestUri.Query does not remove the leading '?'. This causes
             *  problems with hosts that then subsequently join the path and querystring
             *  resulting in a '??' (such as signalr converting the env dict to a ServerRequest) 
             */

            IDictionary<string, object> env = null;
            var handler = new OwinClientHandler(dict =>
            {
                env = dict;
                return TaskHelpers.Completed();
            });
            var httpClient = new HttpClient(handler);
            string query = "a=b";
            httpClient.GetAsync("http://example.com?" + query).Wait();
            Assert.Equal(query, env["owin.RequestQueryString"]);
        }
    }
}
