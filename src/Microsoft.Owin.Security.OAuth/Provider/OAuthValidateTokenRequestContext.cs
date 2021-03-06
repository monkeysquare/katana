// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using Microsoft.Owin.Security.OAuth.Messages;

namespace Microsoft.Owin.Security.OAuth
{
    public class OAuthValidateTokenRequestContext : BaseValidatingContext<OAuthAuthorizationServerOptions>
    {
        public OAuthValidateTokenRequestContext(
            IOwinContext context,
            OAuthAuthorizationServerOptions options,
            TokenEndpointRequest tokenRequest,
            BaseValidatingClientContext clientContext) : base(context, options)
        {
            TokenRequest = tokenRequest;
            ClientContext = clientContext;
        }

        public TokenEndpointRequest TokenRequest { get; private set; }

        public BaseValidatingClientContext ClientContext { get; private set; }
    }
}
